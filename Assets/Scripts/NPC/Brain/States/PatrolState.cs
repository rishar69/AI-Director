using UnityEngine;

/// <summary>
/// Roaming with no idea anyone is here.
///
/// Wanders around the patrol origin, or around the Director's hinted
/// area once Phase 4 supplies one. The hint is used as a WANDER REGION,
/// never as a destination - walking straight to the middle of a hint
/// would be indistinguishable from being told where the player is.
/// </summary>
public class PatrolState : XenoState
{
    private float pauseUntil;

    private float orderExpiry;

    private bool moving;

    private bool usingHint;


    public override XenoStateId Id => XenoStateId.Patrol;


    protected override void OnEnter()
    {
        Locomotion.ClearFacing();

        moving = false;

        usingHint = false;

        pauseUntil = 0f;
    }


    protected override XenoStateId? OnUpdate()
    {
        if (Brain.ShouldInvestigate())
            return XenoStateId.Investigate;

        if (moving)
        {
            TickMoving();

            return null;
        }

        if (Time.time < pauseUntil)
            return null;

        PickNextPoint();

        return null;
    }


    private void TickMoving()
    {
        bool finished =
            Locomotion.HasArrived ||
            Locomotion.PathFailed ||
            Time.time > orderExpiry;

        if (!finished)
            return;

        Locomotion.Stop();

        moving = false;

        if (usingHint && Locomotion.HasArrived)
        {
            /*
             * Tell the Director this area has been looked over so it
             * can suggest somewhere else next time.
             */

            Brain.Hints?.NotifyHintReached();

            usingHint = false;
        }

        pauseUntil =
            Time.time +
            Random.Range(
                Brain.PatrolPauseMin,
                Brain.PatrolPauseMax
            );
    }


    private void PickNextPoint()
    {
        Vector3 origin = Brain.PatrolOrigin;

        float radius = Brain.PatrolRadius;

        usingHint = false;

        if (Brain.Hints != null && Brain.Hints.HasSearchHint)
        {
            origin = Brain.Hints.SearchHintPosition;

            radius = Mathf.Max(1f, Brain.Hints.SearchHintRadius);

            usingHint = true;
        }

        if (!Locomotion.TryGetRandomPoint(
                origin,
                radius,
                out Vector3 point))
        {
            Delay();

            return;
        }

        if (!Locomotion.MoveTo(point, Gait.Prowl))
        {
            Delay();

            return;
        }

        moving = true;

        orderExpiry = Time.time + Brain.OrderTimeout;
    }


    private void Delay()
    {
        usingHint = false;

        pauseUntil = Time.time + 1f;
    }
}
