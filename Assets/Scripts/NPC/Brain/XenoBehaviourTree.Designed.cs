using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The DESIGNED tree: our own root, modelled on MainBehave's shape but
/// not a 1:1 copy.
///
/// Every root child is implemented - there are no stubs to fall through,
/// so there is no gap where the creature has a lead and stands idle.
/// It reuses the ported branches that already behave well (attack,
/// threat, suspect response, hiding player, suspicious items, stalk) and
/// adds or replaces the ones that made it look dumb:
///
///   PULL OUT   it has found you in a hiding place: drag you out. The
///              ported attack refuses hiding targets and the hiding
///              branch refuses while attack is raised, so before this a
///              creature that found you under a desk stood idle beside you.
///
///   HUNT       replaces SEARCH. When contact is lost the creature works
///              the trail: last known position, then the way it saw you
///              moving, then the nearest hiding spots ranked by how likely
///              they are, then a sweep of the area it has not looked at,
///              and only then gives up - by forgetting the lead, which
///              hands over to ROAM.
///
///   APPROACH   safety net: it has a lead and nothing above took it.
///   LEAD       Close on the lead rather than stand still.
///
///   ROAM       replaces BACKSTAGE_AREA_SWEEP. Instead of random points it
///              walks to the part of the map it has gone longest without
///              seeing, warm-biased toward where the last hunt went cold.
///
/// Priority, top first:
///
///   COMMITTED ATTACK > THREAT AWARE > PULL OUT OF HIDING > ATTACK >
///   SUSPECT RESPONSE > PLAYER HIDING > SUSPICIOUS ITEM (high) > HUNT >
///   STALK > SUSPICIOUS ITEM (medium, low) > APPROACH LEAD >
///   PEEK IN PASSING (learned) > ROAM > IDLE
/// </summary>
public partial class XenoBehaviourTree
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Hunt (designed tree)")]
    /// <summary>Give up this long after the last sighting, whatever step the hunt is on.</summary>
    [SerializeField, Min(10f)]
    private float huntGiveUpSeconds = 75f;

    /// <summary>Pause at each place it checks.</summary>
    [SerializeField, Min(0.2f)]
    private float huntLookSeconds = 1.6f;

    /// <summary>Furthest it extrapolates along the heading it last saw you moving.</summary>
    [SerializeField, Min(1f)]
    private float huntMaxProjection = 12f;

    /// <summary>Hiding spots further than this from the estimate are not considered.</summary>
    [SerializeField, Min(2f)]
    private float huntSpotRadius = 11f;

    [SerializeField, Min(1)]
    private int huntMaxSpots = 3;

    /// <summary>How close it stands to a hiding spot to look into it.</summary>
    [SerializeField, Min(0.5f)]
    private float peekDistance = 1.3f;

    [SerializeField, Min(0.2f)]
    private float peekSeconds = 1.2f;

    [SerializeField, Min(0)]
    private int huntSweepPoints = 2;

    [SerializeField, Min(2f)]
    private float huntSweepRadius = 12f;


    [Header("Roam (designed tree)")]
    [SerializeField, Min(0f)]
    private float roamLookSeconds = 2f;

    [SerializeField, Min(1f)]
    private float roamMinimumTravel = 6f;

    /// <summary>For this long after a hunt goes cold, roaming stays near where it lost you.</summary>
    [SerializeField, Min(0f)]
    private float roamWarmSeconds = 35f;

    [SerializeField, Min(2f)]
    private float roamWarmRadius = 18f;


    // =========================================================
    // HUNT PLAN STATE
    // =========================================================

    private enum HuntStep
    {
        GoToLastKnown = 0,
        FollowHeading = 1,
        CheckHidingSpots = 2,
        SweepArea = 3,
        GiveUp = 4,
        Done = 5
    }


    /// <summary>
    /// One hunt's working state. Rebuilt whenever the creature gets a
    /// newer lead than the one the plan was made from, so a fresh noise
    /// mid-search restarts the hunt from that noise.
    /// </summary>
    private class HuntPlan
    {
        public HuntStep Step;
        public float LeadStamp = -1f;
        public Vector3 Lead;
        public Vector3 Estimate;
        public readonly List<HidingSpot> Spots = new List<HidingSpot>();
        public bool SpotsPlanned;
        public int Swept;
    }


    /// <summary>
    /// One walk somewhere, with a time limit. Every walk the designed
    /// tree issues goes through this, so an unreachable point costs a few
    /// seconds rather than leaving the creature pacing in place.
    /// </summary>
    private class Leg
    {
        public bool Active;
        public float Deadline;
        public Vector3 Target;

        public bool Start(BTContext c, Vector3 target, Gait gait)
        {
            if (!c.Locomotion.MoveTo(target, gait))
            {
                Active = false;
                return false;
            }

            Active = true;
            Target = c.Locomotion.Destination;

            float speed = gait == Gait.Run ? 3.5f : gait == Gait.Walk ? 1.6f : 1.0f;

            Deadline = Time.time + 4f + FlatDistance(c.Owner.position, Target) / speed * 1.6f;

            return true;
        }

        /// <summary>Arrived, failed or out of time - either way, this leg is over.</summary>
        public bool Over(BTContext c, float tolerance = 0.8f)
        {
            return c.Locomotion.PathFailed ||
                   c.Locomotion.HasArrived ||
                   FlatDistance(c.Owner.position, Target) < tolerance ||
                   Time.time > Deadline;
        }
    }


    private readonly HuntPlan hunt = new HuntPlan();

    private Vector3 coldTrail;

    private float coldTrailTime = -999f;


    // =========================================================
    // ROOT
    // =========================================================

    private BTSelector BuildDesignedRoot()
    {
        BTSelector root = new BTSelector("CARETAKER_ROOT");

        root.Add(BuildCommittedAttackBranch());
        root.Add(BuildThreatAwareBranch());
        root.Add(BuildPullOutOfHidingBranch());
        root.Add(BuildAttackCoreBranch());
        root.Add(BuildSuspectTargetResponseBranch());
        root.Add(BuildPlayerHideBranch());
        root.Add(BuildSuspiciousItemBranch(SuspiciousItemPriority.High));
        root.Add(BuildHuntBranch());
        root.Add(BuildAreaSweepBranch());
        root.Add(BuildSuspiciousItemBranch(SuspiciousItemPriority.Medium));
        root.Add(BuildSuspiciousItemBranch(SuspiciousItemPriority.Low));
        root.Add(BuildApproachLeadBranch());
        root.Add(BuildRoamPeekBranch());
        root.Add(BuildRoamBranch());
        root.Add(BuildIdleBranch());

        return root;
    }


    /// <summary>
    /// Something is in sight or touching it right now. While it is, the
    /// attack and stalk branches handle the creature; the hunt is for
    /// after.
    ///
    /// Hearing deliberately does NOT count. A sound moves the last known
    /// position, the hunt replans from there and goes to it - which is
    /// how a sound should be handled. Counting hearing here once left the
    /// creature idle while it could plainly hear you.
    /// </summary>
    private static bool ContactIsLive(BTContext c)
    {
        return c.Senses.IsSenseActivationAbove(SenseChannelId.Visual, SenseThreshold.Lower) ||
               c.Senses.IsSenseActivationAbove(SenseChannelId.Touched, SenseThreshold.Activated);
    }


    // =========================================================
    // PULL OUT OF HIDING
    // =========================================================

    /// <summary>
    /// It has found you in a hiding place - seen you there, or is pressed
    /// up against you - so it drags you out.
    /// </summary>
    private BTNode BuildPullOutOfHidingBranch()
    {
        BTSelector confirmed = new BTSelector("SEL_confirmed_in_hiding");
        confirmed.AddRange(
            XenoLeaf.IsSenseActivationAbove(SenseChannelId.Visual, SenseThreshold.Activated),
            XenoLeaf.IsSenseActivationAbove(SenseChannelId.Touched, SenseThreshold.Activated));

        BTSequence strike = new BTSequence("SEQ_drag_out");
        strike.AddRange(
            XenoLeaf.TargetIsWithinDistance(meleeRange + 0.8f),
            XenoLeaf.HasMeleeAttackAvailable(),
            XenoLeaf.TriggerSound("ALIEN_DRAG_OUT"),
            XenoLeaf.MeleeAttackTarget(meleeDuration, attackBanSeconds, Sst.Critical));

        BTSelector act = new BTSelector("SEL_pull_out");
        act.AddRange(
            strike,
            XenoLeaf.MoveThroughTarget(Gait.Run, Sst.Graceful));

        BTSequenceStateless spine = new BTSequenceStateless("PULL_OUT_SPINE");
        spine.AddRange(
            XenoLeaf.HaveTarget(),
            Condition("TargetLogicCharacterFlags(PlayerHiding)", c => c.Blackboard.GetTargetFlag(TargetLogicFlag.PlayerHiding)),
            confirmed,
            XenoLeaf.SetWithdrawState(WithdrawState.NotWithdrawing),
            act);

        return Guarded(Motivation.Attack, new BTBranch("Branch(PULL_OUT_OF_HIDING_BRANCH)", spine));
    }


    // =========================================================
    // HUNT
    // =========================================================

    private BTNode BuildHuntBranch()
    {
        /* Out of time: let go of the lead so ROAM takes over. */
        BTSequence exhausted = new BTSequence("hunt_exhausted");
        exhausted.AddRange(
            Condition("HuntOutOfTime", c => c.Hunt.SecondsSinceSighting > huntGiveUpSeconds),
            BuildHuntGiveUp());

        BTSequenceStateless steps = new BTSequenceStateless("HUNT_STEPS", BTSequence.FailPolicy.IgnoreChildFail);

        steps.AddRange(
            HuntStage(HuntStep.GoToLastKnown, BuildHuntGoToLastKnown()),
            HuntStage(HuntStep.FollowHeading, BuildHuntFollowHeading()),
            HuntStage(HuntStep.CheckHidingSpots, BuildHuntCheckSpots()),
            HuntStage(HuntStep.SweepArea, BuildHuntSweep()),
            HuntStage(HuntStep.GiveUp, BuildHuntGiveUp()));

        /*
         * Stateless, so PlanHunt runs every tick. A stateful sequence
         * resumes at its running child and would skip the planner while
         * a step was under way - a new noise mid-hunt would then never
         * restart the plan from the new lead.
         */
        BTSequenceStateless work = new BTSequenceStateless("hunt_work");
        work.AddRange(
            BuildHuntPlanUpdate(),
            XenoLeaf.SetWithdrawState(WithdrawState.NotWithdrawing),
            new BTTimerScope(10f, TimerId.SuspectTargetResponseDelay,
                new BTTimerScope(5f, TimerId.HeightenedSenses, steps)));

        BTSelector body = new BTSelector("SEL_hunt");
        body.AddRange(exhausted, work);

        BTSequenceStateless spine = new BTSequenceStateless("HUNT_SPINE");
        spine.AddRange(
            XenoLeaf.HaveTarget(),
            Condition("HasHuntMemory", c => c.Hunt != null),
            Condition("ContactLost", c => !ContactIsLive(c)),
            body);

        /*
         * Not motivation-guarded. The driver withholds SEARCH whenever
         * ATTACK is raised, and ATTACK is raised by a loud enough sound
         * alone - while the attack branch itself needs sight. Guarding
         * on SEARCH left a hole where the creature could hear you, could
         * not attack, could not hunt, and stood still.
         */
        return new BTBranch("Branch(HUNT_BRANCH)", spine);
    }


    private BTNode HuntStage(HuntStep step, BTNode action)
    {
        BTSequence s = new BTSequence("step:" + step);

        s.AddRange(
            Condition("ShouldDoStep(" + step + ")", c => hunt.Step == step),
            action,
            new BTAction("DoneStep(" + step + ")", c =>
            {
                if (hunt.Step == step) hunt.Step = step + 1;
                return BTStatus.Success;
            }));

        return s;
    }


    /// <summary>
    /// Starts a new plan whenever the lead is newer than the one the
    /// current plan was built from. Always succeeds.
    /// </summary>
    private BTNode BuildHuntPlanUpdate()
    {
        return new BTAction("PlanHunt", c =>
        {
            float stamp = Time.time - c.Perception.TimeSinceLastKnownPosition;

            if (Mathf.Abs(stamp - hunt.LeadStamp) > 0.05f)
            {
                hunt.LeadStamp = stamp;
                hunt.Lead = c.Perception.LastKnownPosition;
                hunt.Estimate = hunt.Lead;
                hunt.Step = HuntStep.GoToLastKnown;
                hunt.Spots.Clear();
                hunt.SpotsPlanned = false;
                hunt.Swept = 0;

                c.Hunt.BeginHunt();
                c.Hunt.Estimate = hunt.Estimate;
                c.Hunt.HasEstimate = true;
            }

            if (!c.Hunt.HuntStep.StartsWith("CheckHidingSpots") || hunt.Step != HuntStep.CheckHidingSpots)
            {
                c.Hunt.HuntStep = hunt.Step.ToString();
            }

            return BTStatus.Success;
        });
    }


    /// <summary>
    /// Go to where it last sensed you. Runs if the trail is fresh, walks
    /// if it is not - the urgency fades with the trail.
    /// </summary>
    private BTNode BuildHuntGoToLastKnown()
    {
        Leg leg = new Leg();
        float lookUntil = -1f;

        return new BTAction("GoToLastKnownPosition", c =>
        {
            if (lookUntil > 0f)
            {
                if (Time.time < lookUntil) return BTStatus.Running;

                lookUntil = -1f;
                leg.Active = false;
                c.Senses.MarkSearched(hunt.Lead);
                return BTStatus.Success;
            }

            if (!leg.Active)
            {
                Gait gait = c.Hunt.SecondsSinceSighting < 6f ? Gait.Run : Gait.Walk;

                if (!leg.Start(c, hunt.Lead, gait)) return BTStatus.Success;

                return BTStatus.Running;
            }

            if (leg.Over(c, 1.2f))
            {
                c.Locomotion.Stop();
                c.Animation?.RequestLookAround(huntLookSeconds);
                lookUntil = Time.time + huntLookSeconds;
            }

            return BTStatus.Running;
        }, Sst.Graceful, (c, u) =>
        {
            leg.Active = false;
            lookUntil = -1f;
            c.Animation?.CancelLookAround();
        });
    }


    /// <summary>
    /// Carry on the way it saw you moving. Only runs when the last
    /// sightings gave a heading; projects along it by how far you could
    /// have got, stopped short at walls.
    /// </summary>
    private BTNode BuildHuntFollowHeading()
    {
        Leg leg = new Leg();
        float lookUntil = -1f;

        BTAction follow = new BTAction("FollowLastSeenHeading", c =>
        {
            if (lookUntil > 0f)
            {
                if (Time.time < lookUntil) return BTStatus.Running;

                lookUntil = -1f;
                leg.Active = false;
                return BTStatus.Success;
            }

            if (!leg.Active)
            {
                float reach = Mathf.Clamp(c.Hunt.HeadingSpeed * Mathf.Max(1.5f, c.Hunt.SecondsSinceSighting), 3f, huntMaxProjection);

                Vector3 want = hunt.Lead + c.Hunt.Heading * reach;

                if (NavMesh.Raycast(hunt.Lead, want, out NavMeshHit hit, NavMesh.AllAreas))
                {
                    want = hit.position;
                }

                hunt.Estimate = want;
                c.Hunt.Estimate = want;

                if (FlatDistance(want, c.Owner.position) < 1.5f || !leg.Start(c, want, Gait.Walk))
                {
                    return BTStatus.Success;
                }

                return BTStatus.Running;
            }

            if (leg.Over(c))
            {
                c.Locomotion.Stop();
                c.Animation?.RequestLookAround(huntLookSeconds);
                lookUntil = Time.time + huntLookSeconds;
            }

            return BTStatus.Running;
        }, Sst.Graceful, (c, u) =>
        {
            leg.Active = false;
            lookUntil = -1f;
            c.Animation?.CancelLookAround();
        });

        BTSequence withHeading = new BTSequence("if_heading");
        withHeading.AddRange(Condition("HasHeading", c => c.Hunt.HasHeading), follow);

        BTSelector sel = new BTSelector("SEL_heading");
        sel.AddRange(withHeading, Condition("NoHeading", c => true));

        return sel;
    }


    /// <summary>
    /// Check the hiding spots you most likely went to: the one it
    /// suspects first, then those near the estimate and along your
    /// heading, skipping any checked recently, and favouring ones it has
    /// pulled you out of before.
    ///
    /// The check is physical. It walks up to the spot, faces it and
    /// looks in. If someone is in it, that is a sighting at arm's length
    /// and the pull-out branch above takes over on the next tick.
    /// </summary>
    private BTNode BuildHuntCheckSpots()
    {
        PeekJob job = new PeekJob();

        return new BTAction("CheckLikelyHidingSpots", c =>
        {
            if (!hunt.SpotsPlanned)
            {
                PlanSpots(c);
                hunt.SpotsPlanned = true;
            }

            if (!job.Active)
            {
                while (hunt.Spots.Count > 0 && !job.Active)
                {
                    HidingSpot next = hunt.Spots[0];
                    hunt.Spots.RemoveAt(0);

                    if (next == null || c.Hunt.WasCheckedRecently(next)) continue;

                    if (job.Begin(c, next, PeekPoint(next), Gait.Prowl, peekSeconds))
                    {
                        c.Hunt.HuntStep = "CheckHidingSpots: " + next.name;
                    }
                }

                return job.Active ? BTStatus.Running : BTStatus.Success;
            }

            if (job.Tick(c, this) == PeekJob.Result.Found)
            {
                hunt.Spots.Clear();
                return BTStatus.Success;
            }

            return BTStatus.Running;
        }, Sst.Graceful, (c, u) => job.Cancel(c));
    }


    /// <summary>
    /// Walk to a hiding spot, face it, look in. Shared by the hunt and by
    /// the learned habit of peeking into spots while roaming.
    /// </summary>
    private class PeekJob
    {
        public enum Result { Working, Empty, Found }

        public HidingSpot Spot;

        private readonly Leg leg = new Leg();

        private float until = -1f;

        private float seconds;

        public bool Active => Spot != null;

        public bool Begin(BTContext c, HidingSpot spot, Vector3 standAt, Gait gait, float lookSeconds)
        {
            if (!leg.Start(c, standAt, gait))
                return false;

            Spot = spot;
            seconds = lookSeconds;
            until = -1f;
            return true;
        }

        public Result Tick(BTContext c, XenoBehaviourTree tree)
        {
            if (Spot == null)
                return Result.Empty;

            if (leg.Active)
            {
                if (!leg.Over(c, 0.6f))
                    return Result.Working;

                c.Locomotion.Stop();
                c.Locomotion.FaceTowards(Spot.transform.position);
                leg.Active = false;
                until = Time.time + seconds;
                return Result.Working;
            }

            /* Looking in: close enough to see into it? */
            if (Spot.Occupied && FlatDistance(c.Owner.position, Spot.transform.position) <= tree.peekDistance + 1.4f)
            {
                c.Perception.NoteContact(Spot.HidePosition);
                c.Senses.AddImpulse(SenseChannelId.Visual, 4f);
                c.Hunt.NoteFoundIn(Spot);
                c.Hunt.MarkChecked(Spot);
                c.Locomotion.ClearFacing();

                Debug.Log("[Hunt] " + tree.name + " found the target hiding at " + Spot.name, tree);

                Spot = null;
                return Result.Found;
            }

            if (Time.time < until)
                return Result.Working;

            c.Hunt.MarkChecked(Spot);
            c.Hunt.ClearSuspicion(Spot);
            c.Locomotion.ClearFacing();
            Spot = null;
            return Result.Empty;
        }

        public void Cancel(BTContext c)
        {
            Spot = null;
            leg.Active = false;
            until = -1f;
            c.Locomotion.ClearFacing();
        }
    }


    private void PlanSpots(BTContext c)
    {
        hunt.Spots.Clear();

        IReadOnlyList<HidingSpot> all = HidingSpot.Active;

        List<(HidingSpot spot, float score)> scored = new List<(HidingSpot, float)>();

        for (int i = 0; i < all.Count; i++)
        {
            HidingSpot s = all[i];

            if (s == null || c.Hunt.WasCheckedRecently(s)) continue;

            Vector3 p = s.transform.position;

            float fromEstimate = FlatDistance(p, hunt.Estimate);

            if (fromEstimate > huntSpotRadius && s != c.Hunt.SuspectedSpot) continue;

            float score = fromEstimate + FlatDistance(p, c.Owner.position) * 0.35f;

            if (c.Hunt.HasHeading)
            {
                Vector3 dir = p - hunt.Lead;
                dir.y = 0f;

                if (dir.sqrMagnitude > 0.01f)
                {
                    score -= Vector3.Dot(dir.normalized, c.Hunt.Heading) * 3f;
                }
            }

            score -= c.Hunt.TimesFoundIn(s) * 4f;

            /* The spot it already suspects goes to the front of the queue. */
            if (s == c.Hunt.SuspectedSpot) score -= 20f;

            scored.Add((s, score));
        }

        scored.Sort((a, b) => a.score.CompareTo(b.score));

        /* Once it has learned you hide, a hunt is willing to check more places. */
        int limit = huntMaxSpots + (c.Learning != null && c.Learning.HidingAwareness >= 2 ? 2 : 0);

        for (int i = 0; i < scored.Count && i < limit; i++)
        {
            hunt.Spots.Add(scored[i].spot);
        }
    }


    /// <summary>Where to stand to look into a spot: in front of it, on the side it faces.</summary>
    private Vector3 PeekPoint(HidingSpot spot)
    {
        return spot.transform.position + spot.transform.forward * peekDistance;
    }


    /// <summary>
    /// Sweep the nearby cells it has not had eyes on, around the
    /// estimate - the places the hunt has not covered yet.
    /// </summary>
    private BTNode BuildHuntSweep()
    {
        Leg leg = new Leg();
        float lookUntil = -1f;

        return new BTAction("SweepUnseenNearby", c =>
        {
            if (lookUntil > 0f)
            {
                if (Time.time < lookUntil) return BTStatus.Running;

                lookUntil = -1f;
                leg.Active = false;
                hunt.Swept++;
            }

            if (hunt.Swept >= huntSweepPoints) return BTStatus.Success;

            if (!leg.Active)
            {
                if (!c.Hunt.TryPickStaleCell(c.Owner.position, hunt.Estimate, huntSweepRadius, 2.5f, out Vector3 p) ||
                    !leg.Start(c, p, Gait.Prowl))
                {
                    return BTStatus.Success;
                }

                return BTStatus.Running;
            }

            if (leg.Over(c))
            {
                c.Locomotion.Stop();
                c.Animation?.RequestLookAround(huntLookSeconds);
                lookUntil = Time.time + huntLookSeconds;
            }

            return BTStatus.Running;
        }, Sst.Graceful, (c, u) =>
        {
            leg.Active = false;
            lookUntil = -1f;
            c.Animation?.CancelLookAround();
        });
    }


    /// <summary>
    /// The trail is cold. Forget the lead, so HaveTarget fails and the
    /// creature goes back to roaming - starting near here.
    /// </summary>
    private BTNode BuildHuntGiveUp()
    {
        return new BTAction("GiveUpHunt", c =>
        {
            coldTrail = hunt.Estimate;
            coldTrailTime = Time.time;

            c.Senses.MarkSearched(hunt.Estimate);
            c.Perception.ForgetLastKnownPosition();
            c.Hunt.EndHunt();

            hunt.Step = HuntStep.Done;
            hunt.LeadStamp = -1f;

            if (logBranchChanges)
            {
                Debug.Log("[Hunt] " + name + " lost the trail, back to roaming", this);
            }

            return BTStatus.Success;
        });
    }


    // =========================================================
    // APPROACH LEAD (safety net)
    // =========================================================

    /// <summary>
    /// It has a lead and nothing above wanted it - typically a faint
    /// glimpse while the motivations are set for something else. Close
    /// on the lead carefully rather than stand still. This is the rule
    /// the ported tree broke: never idle with a lead.
    /// </summary>
    private BTNode BuildApproachLeadBranch()
    {
        BTAction approach = new BTAction("CloseOnLead", c =>
        {
            Vector3 lead = c.Perception.LastKnownPosition;

            float d = FlatDistance(c.Owner.position, lead);

            if (d < 1.5f)
            {
                c.Locomotion.Stop();
                c.Locomotion.FaceTowards(lead);
                c.Animation?.RequestLookAround(0.5f);
                return BTStatus.Running;
            }

            if (!c.Locomotion.HasDestination || FlatDistance(c.Locomotion.Destination, lead) > 1f)
            {
                c.Locomotion.MoveTo(lead, d < 8f ? Gait.Prowl : Gait.Walk);
            }

            return BTStatus.Running;
        }, Sst.Graceful, (c, u) => c.Locomotion.ClearFacing());

        /* Stateless: the guards in front of the action are re-checked every tick. */
        BTSequenceStateless body = new BTSequenceStateless("APPROACH_LEAD_BODY");
        body.AddRange(XenoLeaf.HaveTarget(), approach);

        return new BTBranch("Branch(APPROACH_LEAD_BRANCH)", body);
    }


    // =========================================================
    // PEEK IN PASSING (learned)
    // =========================================================

    [Header("Learned habits (designed tree)")]
    /// <summary>How far off its route it will go to peek into a hiding spot.</summary>
    [SerializeField, Min(1f)]
    private float passingPeekRadius = 8f;

    /// <summary>Do not re-peek a spot sooner than this while roaming.</summary>
    [SerializeField, Min(1f)]
    private float passingPeekRecheck = 45f;

    /// <summary>Minimum gap between passing peeks, so roaming still covers ground.</summary>
    [SerializeField, Min(0f)]
    private float passingPeekCooldown = 10f;


    /// <summary>
    /// Only once it has LEARNED that this player hides: while roaming
    /// with no lead, it detours to look into hiding spots it passes.
    /// Before that it walks straight past them. This is the visible
    /// moment of the creature adapting - the desk you hid under twice is
    /// now one it checks on its rounds.
    /// </summary>
    private BTNode BuildRoamPeekBranch()
    {
        PeekJob job = new PeekJob();
        float nextAllowed = -1f;

        BTAction peek = new BTAction("PeekIntoSpotsInPassing", c =>
        {
            if (!job.Active)
            {
                if (Time.time < nextAllowed) return BTStatus.Failure;

                HidingSpot best = null;
                float bestD = passingPeekRadius;

                IReadOnlyList<HidingSpot> all = HidingSpot.Active;

                for (int i = 0; i < all.Count; i++)
                {
                    HidingSpot s = all[i];

                    if (s == null || c.Hunt.SecondsSinceChecked(s) < passingPeekRecheck) continue;

                    /* Spots it has pulled you out of before pull harder. */
                    float d = FlatDistance(c.Owner.position, s.transform.position) - c.Hunt.TimesFoundIn(s) * 3f;

                    if (d < bestD)
                    {
                        bestD = d;
                        best = s;
                    }
                }

                if (best == null || !job.Begin(c, best, PeekPoint(best), Gait.Prowl, peekSeconds))
                {
                    nextAllowed = Time.time + 2f;
                    return BTStatus.Failure;
                }

                c.Hunt.HuntStep = "peek in passing: " + best.name;
                return BTStatus.Running;
            }

            if (job.Tick(c, this) == PeekJob.Result.Working)
                return BTStatus.Running;

            nextAllowed = Time.time + passingPeekCooldown;
            return BTStatus.Success;
        }, Sst.Graceful, (c, u) => job.Cancel(c));

        /* Stateless: the guards in front of the action are re-checked every tick. */
        BTSequenceStateless body = new BTSequenceStateless("PEEK_IN_PASSING_BODY");
        body.AddRange(
            Condition("HasLearned(PlayerHides)", c => c.Learning != null && c.Learning.HidingAwareness >= 1),
            new BTInverter(XenoLeaf.HaveTarget()),
            Condition("NoLiveContact", c => !ContactIsLive(c)),
            peek);

        return new BTBranch("Branch(PEEK_IN_PASSING_BRANCH)", body);
    }


    // =========================================================
    // ROAM
    // =========================================================

    /// <summary>
    /// No lead: patrol the map by going where it has gone longest
    /// without looking. Never succeeds - like the sweep it replaces, it
    /// is a role that runs until something outranks it.
    /// </summary>
    private BTNode BuildRoamBranch()
    {
        Leg leg = new Leg();
        float lookUntil = -1f;

        BTAction roam = new BTAction("RoamToLeastRecentlySeen", c =>
        {
            if (c.Hunt == null) return BTStatus.Failure;

            if (lookUntil > 0f)
            {
                if (Time.time < lookUntil) return BTStatus.Running;

                lookUntil = -1f;
                leg.Active = false;
            }

            if (!leg.Active)
            {
                /*
                 * Where to roam, in order of authority:
                 *   1. its own cold trail - where it just lost you;
                 *   2. the Director's area, if it has set one (build-up
                 *      drifts it toward your region, relief sends it away);
                 *   3. anywhere it has not looked for a while.
                 */
                bool warm = Time.time - coldTrailTime < roamWarmSeconds;

                Vector3? focus = null;

                float focusRadius = roamWarmRadius;

                string why = "roam";

                if (warm)
                {
                    focus = coldTrail;
                    why = "roam near cold trail";
                }
                else if (c.Blackboard.HasDirectorArea)
                {
                    focus = c.Blackboard.DirectorAreaCenter;
                    focusRadius = c.Blackboard.DirectorAreaRadius + 3f;
                    why = c.Blackboard.DirectorIntent == DirectorIntent.EaseOff
                        ? "roam away (Director: ease off)"
                        : "roam near the player's area (Director: pressure)";
                }

                if (!c.Hunt.TryPickStaleCell(c.Owner.position, focus, focusRadius, roamMinimumTravel, out Vector3 p) &&
                    !c.Hunt.TryPickStaleCell(c.Owner.position, null, 0f, roamMinimumTravel, out p))
                {
                    if (!c.Locomotion.TryGetRandomPoint(c.Owner.position, 20f, out p)) return BTStatus.Failure;
                }

                if (!leg.Start(c, p, Gait.Prowl)) return BTStatus.Running;

                c.Hunt.HuntStep = why;
                return BTStatus.Running;
            }

            if (leg.Over(c))
            {
                c.Locomotion.Stop();

                if (roamLookSeconds > 0f)
                {
                    c.Animation?.RequestLookAround(roamLookSeconds);
                    lookUntil = Time.time + roamLookSeconds;
                }
                else
                {
                    leg.Active = false;
                }
            }

            return BTStatus.Running;
        }, Sst.Graceful, (c, u) =>
        {
            leg.Active = false;
            lookUntil = -1f;
            c.Animation?.CancelLookAround();
        });

        /* Stateless: the guards in front of the action are re-checked every tick. */
        BTSequenceStateless body = new BTSequenceStateless("ROAM_BODY");
        body.AddRange(Condition("NoLiveContact", c => !ContactIsLive(c)), roam);

        return new BTBranch("Branch(ROAM_BRANCH)", body);
    }
}
