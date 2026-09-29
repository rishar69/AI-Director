using UnityEngine;

/// <summary>
/// Committed. Running the contact down.
///
/// Re-issues the order as the last known position updates, so a target
/// that stays visible gets followed properly rather than the creature
/// running to where they were and stopping.
///
/// It still only ever chases the LAST KNOWN POSITION. Break line of
/// sight and stay quiet, and what it is running at is a memory.
/// </summary>
public class ChaseState : XenoState
{
    private Vector3 issuedFor;

    private float orderExpiry;


    public override XenoStateId Id => XenoStateId.Chase;


    protected override void OnEnter()
    {
        Locomotion.ClearFacing();

        Issue();
    }


    private void Issue()
    {
        if (!Perception.HasLastKnownPosition)
            return;

        issuedFor = Perception.LastKnownPosition;

        Locomotion.MoveTo(issuedFor, Gait.Run);

        orderExpiry = Time.time + Brain.OrderTimeout;
    }


    protected override XenoStateId? OnUpdate()
    {
        if (!Perception.HasLastKnownPosition)
            return XenoStateId.Search;

        Vector3 contact = Perception.LastKnownPosition;

        if (Brain.HasLiveContact &&
            Brain.CanAttack &&
            Brain.DistanceTo(contact) <= Brain.AttackRange)
        {
            return XenoStateId.Attack;
        }

        /*
         * Given up on finding them. Note this uses time since ANY
         * sense fired, not time since it could see them - a target
         * running loudly through the dark stays chased.
         */

        if (Perception.TimeSinceContact > Brain.LoseContactTime)
            return XenoStateId.Search;

        /*
         * A tighter re-issue threshold than Investigate uses. During a
         * chase the target is moving, and a stale destination shows up
         * immediately as the creature veering off.
         */

        if (Vector3.Distance(issuedFor, contact) > 1.0f)
        {
            Issue();

            return null;
        }

        if (Locomotion.PathFailed)
            return XenoStateId.Search;

        if (Locomotion.HasArrived && !Brain.HasLiveContact)
            return XenoStateId.Search;

        if (Time.time > orderExpiry)
            return XenoStateId.Search;

        return null;
    }
}
