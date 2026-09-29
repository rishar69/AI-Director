using UnityEngine;

/// <summary>
/// Driven off. Breaking contact completely.
///
/// Entered when the player deters it - Phase 5's flamethrower - or when
/// the Director decides menace has run too high and the player needs
/// room to breathe. In Phase 6 the destination becomes a vent; for now
/// it simply puts distance between itself and the contact.
///
/// Perception is wiped on the way out. Retreating and then immediately
/// remembering exactly where you were would defeat the point.
/// </summary>
public class RetreatState : XenoState
{
    private bool holding;


    public override XenoStateId Id => XenoStateId.Retreat;


    protected override void OnEnter()
    {
        holding = false;

        Locomotion.ClearFacing();

        FleeFromContact();
    }


    private void FleeFromContact()
    {
        Vector3 away = ChooseFleeDirection();

        Vector3 candidate =
            Brain.transform.position +
            away * Brain.RetreatDistance;

        if (Locomotion.TrySnapToNavMesh(candidate, out Vector3 snapped) &&
            Locomotion.MoveTo(snapped, Gait.Run))
        {
            return;
        }

        /*
         * Cornered. Fall back to any reachable point at all rather than
         * standing still while being burned.
         */

        if (Locomotion.TryGetRandomPoint(
                Brain.transform.position,
                Brain.RetreatDistance,
                out Vector3 fallback))
        {
            Locomotion.MoveTo(fallback, Gait.Run);

            return;
        }

        holding = true;
    }


    private Vector3 ChooseFleeDirection()
    {
        if (!Perception.HasLastKnownPosition)
            return -Brain.transform.forward;

        Vector3 away =
            Brain.transform.position -
            Perception.LastKnownPosition;

        away.y = 0f;

        if (away.sqrMagnitude < 0.01f)
            return -Brain.transform.forward;

        return away.normalized;
    }


    protected override XenoStateId? OnUpdate()
    {
        bool finishedRunning =
            holding ||
            Locomotion.HasArrived ||
            Locomotion.PathFailed;

        /*
         * Stay gone until the deterrent wears off, however long the run
         * itself took.
         */

        if (Brain.IsDeterred)
        {
            if (finishedRunning)
            {
                Locomotion.Stop();
            }

            return null;
        }

        if (Brain.Hints != null && Brain.Hints.ShouldRetreat)
            return null;

        if (!finishedRunning && TimeInState < Brain.RetreatDuration)
            return null;

        /*
         * Come back genuinely ignorant. Anything less and the player
         * gains nothing from having driven it off.
         */

        Perception.ResetPerception();

        Brain.ClearSearchMemory();

        return XenoStateId.Patrol;
    }
}
