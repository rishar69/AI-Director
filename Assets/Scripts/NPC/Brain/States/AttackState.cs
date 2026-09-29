using UnityEngine;

/// <summary>
/// The strike.
///
/// Announces an attack at a position and lets anything that can be
/// hurt work out for itself whether that connects. The brain never
/// touches the player, so it cannot know whether it hit - which is
/// precisely the constraint that keeps the two-brain split intact.
/// </summary>
public class AttackState : XenoState
{
    public override XenoStateId Id => XenoStateId.Attack;


    private Vector3 strikePoint;

    private bool lunging;


    protected override void OnEnter()
    {
        Animation?.CancelLookAround();

        strikePoint =
            Perception.HasLastKnownPosition
                ? Perception.LastKnownPosition
                : Brain.transform.position +
                  Brain.transform.forward;

        lunging = false;

        /*
         * Charge, then plant. Two earlier versions of this were both
         * wrong: stopping on entry left the creature standing in its
         * idle pose for the whole death sequence, and running at the
         * strike point itself put it inside the camera's near plane
         * with nothing framable in shot.
         */

        BeginLunge();

        Animation?.PlayAttack(Brain.AttackDuration);

        Brain.RaiseAttack(strikePoint);
    }


    private void BeginLunge()
    {
        Vector3 from = Brain.transform.position;

        Vector3 delta = strikePoint - from;

        delta.y = 0f;

        float distance = delta.magnitude;

        /*
         * Already inside the standoff - there is nothing to close, and
         * lunging would mean reversing away from the player.
         */

        if (distance <= Brain.AttackStandoff || distance < 0.01f)
        {
            Plant();

            return;
        }

        Vector3 stopShortOf =
            strikePoint -
            delta.normalized * Brain.AttackStandoff;

        if (Locomotion.MoveTo(stopShortOf, Gait.Run))
        {
            lunging = true;

            return;
        }

        Plant();
    }


    /// <summary>
    /// Holds position facing the player for the rest of the strike.
    /// </summary>
    private void Plant()
    {
        lunging = false;

        Locomotion.Stop();

        Locomotion.FaceTowards(strikePoint);
    }


    protected override XenoStateId? OnUpdate()
    {
        if (lunging &&
            (Locomotion.HasArrived || Locomotion.PathFailed))
        {
            Plant();
        }

        if (TimeInState < Brain.AttackDuration)
            return null;

        /*
         * Still sensing something means the strike did not finish the
         * job - go again rather than politely searching.
         */

        if (Brain.HasLiveContact)
            return XenoStateId.Chase;

        return XenoStateId.Search;
    }


    protected override void OnExit()
    {
        Locomotion.ClearFacing();
    }
}
