using UnityEngine;

/// <summary>
/// Walking to the one spot where something registered.
///
/// Deliberately targets the last known position and not wherever the
/// contact currently is. When that position came from a SOUND, the
/// player has usually moved by the time the creature arrives - and
/// arriving somewhere the prey used to be is what makes the alien feel
/// like it is working from evidence rather than from a tracker.
/// </summary>
public class InvestigateState : XenoState
{
    private Vector3 issuedFor;

    private float orderExpiry;

    private bool orderFailed;


    public override XenoStateId Id => XenoStateId.Investigate;


    protected override void OnEnter()
    {
        Locomotion.ClearFacing();

        Issue();
    }


    private void Issue()
    {
        if (!Perception.HasLastKnownPosition)
        {
            orderFailed = true;

            return;
        }

        issuedFor = Perception.LastKnownPosition;

        /*
         * Run when fairly sure, walk when merely curious. A creature
         * that sprints at every distant footstep reads as twitchy
         * rather than dangerous.
         */

        Gait gait =
            Perception.Level >= AwarenessLevel.Alerted
                ? Gait.Run
                : Gait.Walk;

        orderFailed = !Locomotion.MoveTo(issuedFor, gait);

        orderExpiry = Time.time + Brain.OrderTimeout;
    }


    protected override XenoStateId? OnUpdate()
    {
        if (orderFailed)
        {
            /*
             * Nothing walkable near the contact. Mark it so the same
             * impossible point is not retried forever.
             */

            if (Perception.HasLastKnownPosition)
            {
                Brain.MarkSearched(Perception.LastKnownPosition);
            }

            Perception.ForgetLastKnownPosition();

            return XenoStateId.Patrol;
        }

        if (!Perception.HasLastKnownPosition)
            return XenoStateId.Search;

        /*
         * Escalate the moment it is both certain and actually sensing
         * something right now.
         */

        if (Brain.HasLiveContact &&
            Perception.Level >= Brain.StalkFrom)
        {
            return Brain.ChooseEngagement();
        }

        if (Perception.Level < Brain.InvestigateFrom &&
            !Brain.HasLiveContact)
        {
            return XenoStateId.Patrol;
        }

        /*
         * Contact has moved somewhere meaningfully different - redirect
         * rather than finish walking to a stale point.
         */

        if (Vector3.Distance(
                issuedFor,
                Perception.LastKnownPosition) > 1.5f)
        {
            Issue();

            return null;
        }

        if (Locomotion.HasArrived)
            return XenoStateId.Search;

        if (Locomotion.PathFailed || Time.time > orderExpiry)
            return XenoStateId.Search;

        return null;
    }
}
