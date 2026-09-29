using UnityEngine;

/// <summary>
/// Knows roughly where the prey is, and deliberately does not attack.
///
/// THIS IS THE STATE THAT MAKES IT A XENOMORPH.
///
/// Anything can run at a player. What makes the creature frightening is
/// the stretch where it clearly knows, is clearly capable, and is
/// choosing to wait - circling on a ring at stalkDistance, appearing at
/// the edge of vision, refusing to close.
///
/// Patience is finite. When stalkDuration runs out, or the Director
/// says commit, or the prey wanders into reach, it stops playing.
/// </summary>
public class StalkState : XenoState
{
    private float commitAt;

    private float repositionAt;

    private float orderExpiry;


    public override XenoStateId Id => XenoStateId.Stalk;


    protected override void OnEnter()
    {
        commitAt = Time.time + Brain.StalkDuration;

        /*
         * Reposition immediately on entry so it moves onto the ring
         * rather than standing wherever it happened to arrive.
         */

        repositionAt = 0f;
    }


    protected override XenoStateId? OnUpdate()
    {
        if (!Perception.HasLastKnownPosition)
            return XenoStateId.Search;

        Vector3 contact = Perception.LastKnownPosition;

        float distance = Brain.DistanceTo(contact);

        /*
         * Wandered into reach. Even a patient predator takes a free
         * opportunity.
         */

        if (Brain.HasLiveContact &&
            Brain.CanAttack &&
            distance <= Brain.AttackRange)
        {
            return XenoStateId.Attack;
        }

        /*
         * It has them dead to rights now. Whatever uncertainty made
         * circling the right move is gone, and continuing to orbit
         * something it can plainly see reads as broken rather than
         * patient - so the stalk ends the instant awareness reaches
         * chaseFrom.
         */

        if (Brain.HasLiveContact &&
            Perception.Level >= Brain.ChaseFrom)
        {
            return XenoStateId.Chase;
        }

        if (Time.time >= commitAt)
            return XenoStateId.Chase;

        if (Brain.Hints != null && Brain.Hints.ShouldCommit)
            return XenoStateId.Chase;

        /*
         * Too far for circling to mean anything - close the gap.
         */

        if (distance > Brain.StalkBreakDistance)
            return XenoStateId.Chase;

        /*
         * Lost the thread entirely. Stalking something it can no longer
         * sense is just standing still.
         */

        if (!Brain.HasLiveContact &&
            Perception.Level < Brain.StalkFrom)
        {
            return XenoStateId.Search;
        }

        TickCircling(contact);

        return null;
    }


    private void TickCircling(Vector3 contact)
    {
        bool needsNewPoint =
            Time.time >= repositionAt ||
            Locomotion.HasArrived ||
            Locomotion.PathFailed ||
            Time.time > orderExpiry;

        if (needsNewPoint)
        {
            Reposition(contact);
        }

        /*
         * Keep it looking at the prey whenever it is not walking. Being
         * watched from across the room is the whole effect.
         */

        if (!Locomotion.IsMoving)
        {
            Locomotion.FaceTowards(contact);
        }
    }


    /// <summary>
    /// Picks a fresh point on the ring around the contact.
    /// </summary>
    private void Reposition(Vector3 contact)
    {
        repositionAt =
            Time.time + Brain.StalkRepositionInterval;

        orderExpiry = Time.time + Brain.OrderTimeout;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = Random.Range(0f, 360f);

            Vector3 offset =
                Quaternion.Euler(0f, angle, 0f) *
                Vector3.forward *
                Brain.StalkDistance;

            Vector3 candidate = contact + offset;

            if (!Locomotion.TrySnapToNavMesh(
                    candidate,
                    out Vector3 snapped))
            {
                continue;
            }

            if (Locomotion.MoveTo(snapped, Gait.Prowl))
                return;
        }

        /*
         * Nowhere on the ring is reachable - the prey is probably
         * somewhere the creature cannot follow. Hold and watch rather
         * than jitter against the geometry.
         */

        Locomotion.Stop();
    }
}
