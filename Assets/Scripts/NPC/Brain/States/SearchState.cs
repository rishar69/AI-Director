using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sweeping several points around a contact that has gone cold.
///
/// The difference between this and Investigate is the difference
/// between "something happened over there" and "they were here a moment
/// ago and must be close". Checking a handful of spots around the
/// origin, pausing to look at each, is what stops the player feeling
/// safe the instant they break line of sight.
/// </summary>
public class SearchState : XenoState
{
    private Vector3 origin;

    private int visited;

    private float pointExpiry;

    private float lookUntil;

    private bool looking;

    /*
     * Hiding places near the search origin, checked before any random
     * point. Reused between entries so a sweep allocates nothing.
     */

    private readonly List<HidingSpot> nearbyHidingSpots =
        new List<HidingSpot>();

    private int hidingSpotsChecked;


    public override XenoStateId Id => XenoStateId.Search;


    protected override void OnEnter()
    {
        origin =
            Perception.HasLastKnownPosition
                ? Perception.LastKnownPosition
                : Brain.transform.position;

        visited = 0;

        looking = false;

        hidingSpotsChecked = 0;

        nearbyHidingSpots.Clear();

        if (Brain.CheckHidingSpots)
        {
            HidingSpot.CollectNear(
                origin,
                Brain.SearchRadius,
                nearbyHidingSpots
            );
        }

        MoveToNextPoint();
    }


    protected override XenoStateId? OnUpdate()
    {
        XenoStateId? escalation = CheckEscalation();

        if (escalation.HasValue)
            return escalation;

        if (looking)
        {
            if (Time.time < lookUntil)
                return null;

            looking = false;

            if (visited >= Brain.SearchPoints)
                return Finish();

            MoveToNextPoint();

            return null;
        }

        bool reached =
            Locomotion.HasArrived ||
            Locomotion.PathFailed ||
            Time.time > pointExpiry;

        if (reached)
        {
            BeginLook();
        }

        return null;
    }


    /// <summary>
    /// Whether something has happened that makes searching pointless.
    /// </summary>
    private XenoStateId? CheckEscalation()
    {
        if (Brain.HasLiveContact &&
            Perception.Level >= Brain.StalkFrom)
        {
            return Brain.ChooseEngagement();
        }

        /*
         * A fresh contact well outside the area being swept is worth
         * abandoning the sweep for.
         */

        if (Perception.HasLastKnownPosition &&
            Vector3.Distance(
                origin,
                Perception.LastKnownPosition) > Brain.SearchRadius &&
            !Brain.IsRecentlySearched(Perception.LastKnownPosition))
        {
            return XenoStateId.Investigate;
        }

        return null;
    }


    private void MoveToNextPoint()
    {
        visited++;

        if (!TryPickPoint(out Vector3 point))
        {
            BeginLook();

            return;
        }

        if (!Locomotion.MoveTo(point, Gait.Walk))
        {
            BeginLook();

            return;
        }

        pointExpiry = Time.time + Brain.OrderTimeout;
    }


    /// <summary>
    /// Hiding places first, then random ground.
    ///
    /// Checking cover before open floor is both what a hunter would do
    /// and what makes hiding a real decision rather than a free escape.
    /// </summary>
    private bool TryPickPoint(out Vector3 point)
    {
        while (hidingSpotsChecked < nearbyHidingSpots.Count)
        {
            HidingSpot spot =
                nearbyHidingSpots[hidingSpotsChecked];

            hidingSpotsChecked++;

            if (spot == null)
                continue;

            /*
             * Snapped, because a hide point is usually inside the
             * locker and therefore off the NavMesh.
             */

            if (Locomotion.TrySnapToNavMesh(
                    spot.HidePosition,
                    out point))
            {
                return true;
            }
        }

        return Locomotion.TryGetRandomPoint(
            origin,
            Brain.SearchRadius,
            out point
        );
    }


    private void BeginLook()
    {
        Locomotion.Stop();

        looking = true;

        lookUntil = Time.time + Brain.SearchLookDuration;

        Animation?.RequestLookAround(Brain.SearchLookDuration);

        /*
         * Face somewhere other than the way it came, so the scan reads
         * as looking around rather than staring at a wall.
         */

        Vector3 glance =
            Brain.transform.position +
            Quaternion.Euler(0f, Random.Range(-120f, 120f), 0f) *
            Brain.transform.forward * 5f;

        Locomotion.FaceTowards(glance);
    }


    private XenoStateId Finish()
    {
        /*
         * Remember the area as checked so re-detecting a stale position
         * does not send it straight back here.
         */

        Brain.MarkSearched(origin);

        Perception.ForgetLastKnownPosition();

        return XenoStateId.Patrol;
    }
}
