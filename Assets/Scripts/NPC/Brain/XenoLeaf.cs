using UnityEngine;

/// <summary>
/// Every leaf the behaviour tree can use, named exactly as the CATHODE
/// exports name them.
///
/// Branch code then reads like the graph it was taken from, which is
/// the whole point - a mismatch between the two is a bug that is very
/// hard to see otherwise.
///
/// TARGET POSITION IS ALWAYS THE LAST KNOWN POSITION. There is no
/// player reference here and there must never be one. When a leaf asks
/// "how far is the target", it means "how far is the place I last
/// sensed something", which is why the creature can lunge at where you
/// were rather than where you are.
/// </summary>
public static class XenoLeaf
{
    // =========================================================
    // MOTIVATION, FLAGS, TIMERS
    // =========================================================

    public static BTNode HasMotivation(Motivation motivation)
    {
        return new BTCondition(
            "HasMotivation(" + motivation + ")",
            c => c.Blackboard.HasMotivation(motivation)
        );
    }


    public static BTNode LogicCharacterFlags(LogicFlag flag)
    {
        return new BTCondition(
            "LogicCharacterFlags(" + flag + ")",
            c => c.Blackboard.GetFlag(flag)
        );
    }


    public static BTNode TargetLogicCharacterFlags(
        TargetLogicFlag flag)
    {
        return new BTCondition(
            "TargetLogicCharacterFlags(" + flag + ")",
            c => c.Blackboard.GetTargetFlag(flag)
        );
    }


    public static BTNode SetLogicCharacterFlags(
        LogicFlag flag,
        bool value)
    {
        return new BTAction(
            "SetLogicCharacterFlags(" + flag + ", " + value + ")",
            c =>
            {
                c.Blackboard.SetFlag(flag, value);

                return BTStatus.Success;
            }
        );
    }


    public static BTNode SetFrameFlag(FrameFlag flag)
    {
        return new BTAction(
            "SetFrameFlag(" + flag + ")",
            c =>
            {
                c.Blackboard.SetFrameFlag(flag);

                return BTStatus.Success;
            }
        );
    }


    public static BTNode IsFrameFlagSet(FrameFlag flag)
    {
        return new BTCondition(
            "IsFrameFlagSet(" + flag + ")",
            c => c.Blackboard.GetFrameFlag(flag)
        );
    }


    public static BTNode HasTimerExpired(TimerId timer)
    {
        return new BTCondition(
            "HasTimerExpired(" + timer + ")",
            c => c.Blackboard.HasTimerExpired(timer)
        );
    }


    public static BTNode StartTimer(
        float seconds,
        TimerId timer)
    {
        return new BTAction(
            "StartTimer(" + seconds + ", " + timer + ")",
            c =>
            {
                c.Blackboard.StartTimer(seconds, timer);

                return BTStatus.Success;
            }
        );
    }


    public static BTNode ExpireTimer(TimerId timer)
    {
        return new BTAction(
            "ExpireTimer(" + timer + ")",
            c =>
            {
                c.Blackboard.ExpireTimer(timer);

                return BTStatus.Success;
            }
        );
    }


    public static BTNode SetWithdrawState(WithdrawState state)
    {
        return new BTAction(
            "SetWithdrawState(" + state + ")",
            c =>
            {
                c.Blackboard.SetWithdrawState(state);

                return BTStatus.Success;
            }
        );
    }


    public static BTNode AlienIsAllowed(AlienPermission permission)
    {
        return new BTCondition(
            "AlienIsAllowed(" + permission + ")",
            c => c.Blackboard.AlienIsAllowed(permission)
        );
    }


    public static BTNode IsGaugeAmountAbove(
        GaugeId gauge,
        GaugeLevel level)
    {
        return new BTCondition(
            "IsGaugeAmountAbove(" + gauge + ", " + level + ")",
            c => c.Blackboard.IsGaugeAmountAbove(gauge, level)
        );
    }


    public static BTNode IsPerformingRoleOrCouldPerformRole(Role role)
    {
        return new BTCondition(
            "IsPerformingRoleOrCouldPerformRole(" + role + ")",
            c => c.Blackboard
                  .IsPerformingRoleOrCouldPerformRole(role)
        );
    }


    public static BTNode HasDoneSuspectResponseMoveTo()
    {
        return new BTCondition(
            "HasDoneSuspectResponseMoveTo",
            c => c.Blackboard.HasDoneSuspectResponseMoveTo
        );
    }


    public static BTNode HasDoneSuspectResponseWithinTime(
        float seconds)
    {
        return new BTCondition(
            "HasDoneSuspectResponseWithinTime(" + seconds + ")",
            c => c.Blackboard
                  .HasDoneSuspectResponseWithinTime(seconds)
        );
    }


    // =========================================================
    // SENSES
    // =========================================================

    public static BTNode IsSenseActivationAbove(
        SenseChannelId channel,
        SenseThreshold threshold)
    {
        return new BTCondition(
            "IsSenseActivationAbove(" + channel + ", " +
            threshold + ")",
            c => c.Senses.IsSenseActivationAbove(channel, threshold)
        );
    }


    public static BTNode IsAnySenseActivationAbove(
        SenseThreshold threshold)
    {
        return new BTCondition(
            "IsAnySenseActivationAbove(" + threshold + ")",
            c => c.Senses.IsAnySenseActivationAbove(threshold)
        );
    }


    public static BTNode LastTimeSensed(
        SenseChannelId channel,
        SenseThreshold threshold,
        SenseTimeWindow window)
    {
        return new BTCondition(
            "LastTimeSensed(" + channel + ", " + threshold + ", " +
            window + ")",
            c => c.Senses.LastTimeSensed(channel, threshold, window)
        );
    }


    /// <summary>
    /// The creature's BELIEF that the target is hiding - not the fact.
    ///
    /// The name in the export is the whole point: "AlienVersion". The
    /// player's own PlayerHiding component publishes PLAYER_HIDING onto
    /// the blackboard when it enters cover, and the creature reads that
    /// belief only while it still has a contact to attach it to. Lose
    /// the last known position and the creature stops believing you are
    /// hiding, because it no longer has anywhere to believe it ABOUT.
    ///
    /// That indirection is what keeps the two-brain split honest. The
    /// tree never asks the player anything; it reads a flag somebody
    /// else was entitled to write.
    /// </summary>
    public static BTNode AlienVersionPlayerIsHiding()
    {
        return new BTCondition(
            "AlienVersionPlayerIsHiding",
            c =>
                c.Perception.HasLastKnownPosition &&
                c.Blackboard.GetTargetFlag(
                    TargetLogicFlag.PlayerHiding)
        );
    }


    public static BTNode HasAnySenseBeenAboveWithinTime(
        float seconds,
        SenseThreshold threshold)
    {
        return new BTCondition(
            "HasAnySenseBeenAboveWithinTime(" + seconds + ", " +
            threshold + ")",
            c => c.Senses.HasAnySenseBeenAboveWithinTime(
                seconds, threshold)
        );
    }


    public static BTNode LastTimeSearchedWithinTime(float seconds)
    {
        return new BTCondition(
            "LastTimeSearchedWithinTime(" + seconds + ")",
            c => c.Senses.LastTimeSearchedWithinTime(seconds)
        );
    }


    /// <summary>
    /// Whether the creature has already been to the place it last
    /// sensed something.
    ///
    /// This is what ENDS a systematic search. The precondition wires
    /// its False output onward, so searching continues only while there
    /// is still an unvisited contact to walk to.
    /// </summary>
    public static BTNode HasSearchedMostRecentSensedPosition(
        float tolerance)
    {
        return new BTCondition(
            "HasSearchedMostRecentSensedPosition",
            c =>
                c.Perception.HasLastKnownPosition &&
                c.Senses.HasSearchedPosition(
                    c.Perception.LastKnownPosition, tolerance)
        );
    }


    /// <summary>
    /// EventAOccuredAfterB(A, B) - "did A happen more recently than B".
    ///
    /// All six uses live in alien_all_search_variants, where together
    /// they sort hiding, suspicious items, searching and the suspect
    /// response by recency rather than by fixed priority. Each branch
    /// asks to be more recent than everything beneath it.
    ///
    /// Fails when the log is missing, which leaves the branches in
    /// plain priority order - the behaviour they had before this
    /// existed.
    /// </summary>
    public static BTNode EventAOccuredAfterB(
        XenoEvent a,
        XenoEvent b)
    {
        return new BTCondition(
            "EventAOccuredAfterB(" + a + ", " + b + ")",
            c => c.Events != null && c.Events.OccurredAfter(a, b)
        );
    }


    public static BTNode WasSenseThresholdLastIncreaseActivationAbove(
        SenseChannelId channel,
        SenseThreshold threshold)
    {
        return new BTCondition(
            "WasSenseThresholdLastIncreaseActivationAbove(" +
            channel + ", " + threshold + ")",
            c => c.Senses
                  .WasSenseThresholdLastIncreaseActivationAbove(
                      channel, threshold)
        );
    }


    // =========================================================
    // TARGET
    // =========================================================

    public static BTNode HaveTarget()
    {
        return new BTCondition(
            "HaveTarget",
            c => c.Perception.HasLastKnownPosition
        );
    }


    /// <summary>
    /// Only the player is ever a target here. Kept as a named leaf so
    /// the branch structure matches, and so it has somewhere to grow if
    /// other NPCs are ever added.
    /// </summary>
    public static BTNode IsEnemyOfTarget()
    {
        return new BTCondition("IsEnemyOfTarget", c => true);
    }


    public static BTNode TargetIsPlayer()
    {
        return new BTCondition("TargetIsPlayer", c => true);
    }


    public static BTNode HasValidRouteToTarget()
    {
        return new BTCondition(
            "HasValidRouteToTarget",
            c =>
            {
                if (!c.Perception.HasLastKnownPosition)
                    return false;

                return c.Locomotion.TrySnapToNavMesh(
                    c.Perception.LastKnownPosition,
                    out _
                );
            }
        );
    }


    public static BTNode TargetIsWithinDistance(float metres)
    {
        return new BTCondition(
            "TargetIsWithinDistance(" + metres + ")",
            c =>
                c.Perception.HasLastKnownPosition &&
                Flat(c.Owner.position,
                     c.Perception.LastKnownPosition) <= metres
        );
    }


    /// <summary>
    /// Straight line distance stands in for path length. A true routing
    /// distance would need a full NavMesh path each tick, and the tree
    /// asks this often enough that the cost would show.
    /// </summary>
    public static BTNode TargetIsWithinRoutingDistance(float metres)
    {
        return new BTCondition(
            "TargetIsWithinRoutingDistance(" + metres + ")",
            c =>
                c.Perception.HasLastKnownPosition &&
                Flat(c.Owner.position,
                     c.Perception.LastKnownPosition) <= metres
        );
    }


    public static BTNode TargetIsWithinDistanceUnobscured(
        float metres,
        LayerMask obstacles)
    {
        return new BTCondition(
            "TargetIsWithinDistanceUnobscured(" + metres + ")",
            c =>
            {
                if (!c.Perception.HasLastKnownPosition)
                    return false;

                Vector3 eye = c.Owner.position + Vector3.up * 1.6f;

                Vector3 target = c.Perception.LastKnownPosition;

                Vector3 delta = target - eye;

                float distance = delta.magnitude;

                if (distance > metres)
                    return false;

                return !Physics.Raycast(
                    eye,
                    delta / Mathf.Max(0.01f, distance),
                    distance - 0.1f,
                    obstacles,
                    QueryTriggerInteraction.Ignore
                );
            }
        );
    }


    public static BTNode AngleToTargetLessThan(float degrees)
    {
        return new BTCondition(
            "AngleToTargetLessThan(" + degrees + ")",
            c =>
            {
                if (!c.Perception.HasLastKnownPosition)
                    return false;

                Vector3 delta =
                    c.Perception.LastKnownPosition - c.Owner.position;

                delta.y = 0f;

                if (delta.sqrMagnitude < 0.01f)
                    return true;

                return Vector3.Angle(c.Owner.forward, delta) <=
                       degrees;
            }
        );
    }


    // =========================================================
    // TARGET WEAPON
    // =========================================================

    public static BTNode TargetsWeaponHasProperty(string property)
    {
        return new BTCondition(
            "TargetsWeaponHasProperty(" + property + ")",
            c => c.Blackboard.TargetWeaponIsThreat
        );
    }


    public static BTNode TargetsWeaponHasAmmo()
    {
        return new BTCondition(
            "TargetsWeaponHasAmmo",
            c => c.Blackboard.TargetWeaponHasAmmo
        );
    }


    /// <summary>
    /// Is the target pointing its weapon at the creature.
    ///
    /// The mirror of AngleToTargetLessThan: that one asks whether the
    /// creature faces the player, this asks whether the player faces
    /// the creature. The threat branch needs both, which is what makes
    /// it a standoff rather than an ambush.
    /// </summary>
    public static BTNode AngleNPCToTargetsAimLessThan(float degrees)
    {
        return new BTCondition(
            "AngleNPCToTargetsAimLessThan(" + degrees + ")",
            c =>
            {
                if (!c.Perception.HasLastKnownPosition)
                    return false;

                Vector3 toCreature =
                    c.Owner.position - c.Perception.LastKnownPosition;

                toCreature.y = 0f;

                if (toCreature.sqrMagnitude < 0.01f)
                    return true;

                Vector3 aim = c.Blackboard.TargetAimDirection;

                aim.y = 0f;

                if (aim.sqrMagnitude < 0.01f)
                    return false;

                return Vector3.Angle(aim, toCreature) <= degrees;
            }
        );
    }


    // =========================================================
    // WORLD
    // =========================================================

    /// <summary>
    /// For OWNER this is real - the creature knows when it is crossing
    /// a vent link. For TARGET it is always false: the player cannot
    /// enter vents in this game, so no branch gated on it can fire.
    /// </summary>
    public static BTNode IsInVent(Subject subject)
    {
        return new BTCondition(
            "IsInVent(" + subject + ")",
            c =>
                subject == Subject.Owner &&
                c.Locomotion.IsTraversingLink
        );
    }


    public static BTNode IsInCrawlSpace(Subject subject)
    {
        return new BTCondition(
            "IsInCrawlSpace(" + subject + ")",
            c => false
        );
    }


    /// <summary>
    /// False until the level has backstage volumes. Several branches
    /// gate on it, and they will simply never fire until it does.
    /// </summary>
    public static BTNode IsBackstage()
    {
        return new BTCondition("IsBackstage", c => false);
    }


    public static BTNode HasMeleeAttackAvailable()
    {
        return new BTCondition(
            "HasMeleeAttackAvailable(ANY)",
            c => c.Blackboard.HasTimerExpired(TimerId.AttackBan)
        );
    }


    /// <summary>
    /// Debug hooks in the exports. Always false here.
    /// </summary>
    public static BTNode DebugMenuLinkTest(string label)
    {
        return new BTCondition(
            "DebugMenuLinkTest(" + label + ")",
            c => false
        );
    }


    // =========================================================
    // ACTIONS
    // =========================================================

    public static BTNode TriggerSound(string soundId)
    {
        return new BTAction(
            "TriggerSound(" + soundId + ")",
            c =>
            {
                /* Audio clips are not authored yet. */

                return BTStatus.Success;
            }
        );
    }


    /// <summary>
    /// Charges the last known position and keeps going through it,
    /// rather than stopping short.
    /// </summary>
    public static BTNode MoveThroughTarget(
        Gait gait,
        Sst urgency)
    {
        Vector3 issuedFor = Vector3.zero;

        bool issued = false;

        return new BTAction(
            "MoveThroughTarget(" + gait + ", " + urgency + ")",

            c =>
            {
                if (!c.Perception.HasLastKnownPosition)
                    return BTStatus.Failure;

                Vector3 target = c.Perception.LastKnownPosition;

                bool stale =
                    !issued ||
                    Flat(issuedFor, target) > 1.0f ||
                    c.Locomotion.PathFailed;

                if (stale)
                {
                    if (!c.Locomotion.MoveTo(target, gait))
                        return BTStatus.Failure;

                    issuedFor = target;

                    issued = true;
                }

                return BTStatus.Running;
            },

            urgency,

            (c, u) =>
            {
                issued = false;

                if (u == Sst.Critical)
                {
                    c.Locomotion.Stop();
                }
            }
        );
    }


    /// <summary>
    /// The strike. Announces itself and lets anything that can be hurt
    /// decide whether it connected - the creature never finds out.
    /// </summary>
    public static BTNode MeleeAttackTarget(
        float duration,
        float banAfter,
        Sst urgency)
    {
        float until = -1f;

        return new BTAction(
            "MeleeAttackTarget(ANY, " + urgency + ")",

            c =>
            {
                if (until < 0f)
                {
                    Vector3 target =
                        c.Perception.HasLastKnownPosition
                            ? c.Perception.LastKnownPosition
                            : c.Owner.position + c.Owner.forward;

                    c.Locomotion.FaceTowards(target);

                    c.Animation?.PlayAttack(duration);

                    XenoBehaviourTree tree =
                        c.Owner.GetComponent<XenoBehaviourTree>();

                    tree?.RaiseAttack(target);

                    c.Blackboard.StartTimer(banAfter, TimerId.AttackBan);

                    /*
                     * The attack has been served, so the commitment
                     * ends here. Leaving SHOULD_ATTACK latched would
                     * pin the creature in the committed-attack entry
                     * above the withdrawal and it could never be
                     * called off again.
                     */

                    c.Blackboard.SetFlag(LogicFlag.ShouldAttack, false);

                    until = Time.time + duration;
                }

                if (Time.time < until)
                    return BTStatus.Running;

                until = -1f;

                return BTStatus.Success;
            },

            urgency,

            (c, u) =>
            {
                until = -1f;

                c.Locomotion.ClearFacing();
            }
        );
    }


    /// <summary>
    /// Holds a wary distance from an armed target, facing it.
    ///
    /// Not a retreat and not an attack - the creature keeps the target
    /// in front of it and refuses to close while the weapon is pointed
    /// its way. Backs off if the target advances, holds otherwise.
    /// </summary>
    public static BTNode ThreatAware(
        float standoff,
        Sst urgency)
    {
        return new BTAction(
            "ThreatAware(" + urgency + ")",

            c =>
            {
                if (!c.Perception.HasLastKnownPosition)
                    return BTStatus.Failure;

                Vector3 target = c.Perception.LastKnownPosition;

                float distance = Flat(c.Owner.position, target);

                if (distance < standoff - 0.5f)
                {
                    Vector3 away =
                        (c.Owner.position - target);

                    away.y = 0f;

                    if (away.sqrMagnitude > 0.01f)
                    {
                        Vector3 retreatTo =
                            target +
                            away.normalized * standoff;

                        if (c.Locomotion.TrySnapToNavMesh(
                                retreatTo,
                                out Vector3 snapped))
                        {
                            c.Locomotion.MoveTo(snapped, Gait.Walk);
                        }
                    }
                }
                else
                {
                    /*
                     * Unconditionally, because the branch this took
                     * over from was very likely a charge. A graceful
                     * abort leaves the agent coasting toward its old
                     * destination, so holding position means actively
                     * cancelling it - not just declining to move.
                     */

                    c.Locomotion.Stop();
                }

                if (!c.Locomotion.IsMoving)
                {
                    c.Locomotion.FaceTowards(target);
                }

                return BTStatus.Running;
            },

            urgency,

            (c, u) => c.Locomotion.ClearFacing()
        );
    }


    /// <summary>
    /// The "what was that?" beat.
    ///
    /// Something registered, but only at LOWER threshold - not enough
    /// to attack, not nothing either. The creature plants, snaps round
    /// to face where it came from, and holds there.
    ///
    /// It deliberately does NOT walk over. Going to look is a separate
    /// behaviour in the export - PerformRole(SUSPECT_RESPONSE_MOVE_TO),
    /// reached from the search branch - and keeping them apart is what
    /// makes the creature stop dead and stare at a noise instead of
    /// immediately trudging toward it.
    /// </summary>
    public static BTNode SuspectTargetResponse(
        float duration,
        Sst urgency)
    {
        float until = -1f;

        return new BTAction(
            "SuspectTargetResponse(" + urgency + ")",

            c =>
            {
                if (!c.Perception.HasLastKnownPosition)
                    return BTStatus.Failure;

                Vector3 source = c.Perception.LastKnownPosition;

                if (until < 0f)
                {
                    c.Locomotion.Stop();

                    until = Time.time + duration;

                    c.Events?.Record(
                        XenoEvent.SuspectTargetResponse);
                }

                /*
                 * Re-requested every tick, not once on entry.
                 *
                 * Stop() only tells the agent to stop - it keeps
                 * coasting for a few frames while it decelerates, and
                 * the animator drops a pending look-around the moment
                 * it sees the creature still moving. Asking again each
                 * tick means the scan starts as soon as the feet
                 * actually stop, instead of being thrown away during
                 * the slowdown and never playing at all.
                 */

                c.Animation?.RequestLookAround(
                    Mathf.Max(0.05f, until - Time.time));

                c.Locomotion.FaceTowards(source);

                if (Time.time < until)
                    return BTStatus.Running;

                until = -1f;

                return BTStatus.Success;
            },

            urgency,

            (c, u) =>
            {
                until = -1f;

                c.Animation?.CancelLookAround();

                c.Locomotion.ClearFacing();
            }
        );
    }


    /// <summary>
    /// Vent-specific flinch. Nothing to do until the creature can
    /// occupy vents as a space rather than crossing them as a link.
    /// </summary>
    public static BTNode AffectedByFlameThrowerInVent(Sst urgency)
    {
        return new BTNotImplemented(
            "AffectedByFlameThrowerInVent(" + urgency + ")"
        );
    }


    // =========================================================
    // WITHDRAWAL
    // =========================================================

    /// <summary>
    /// ScriptedWithdrawRequested - the Director's one real lever.
    ///
    /// True from the moment the Director asks for a withdrawal until it
    /// decides the withdrawal has been served. Because the branch it
    /// gates sits at y=2564, ABOVE threat awareness and attacking, this
    /// is the only thing in the tree that can call the creature off
    /// mid-hunt.
    /// </summary>
    public static BTNode ScriptedWithdrawRequested()
    {
        return new BTCondition(
            "ScriptedWithdrawRequested",
            c =>
                c.Blackboard.Withdraw !=
                WithdrawState.NotWithdrawing
        );
    }


    /// <summary>
    /// RangeTestForScriptedWithdrawal - "am I far enough out yet".
    ///
    /// Measured against the last known position, not the player. The
    /// creature decides it has got clear of where it last knew you to
    /// be, which is all it is entitled to know.
    /// </summary>
    public static BTNode RangeTestForScriptedWithdrawal(
        float metres)
    {
        return new BTCondition(
            "RangeTestForScriptedWithdrawal(" + metres + ")",
            c =>
                !c.Perception.HasLastKnownPosition ||
                Flat(c.Owner.position,
                     c.Perception.LastKnownPosition) >= metres
        );
    }


    /// <summary>
    /// Walk away from the last known position.
    ///
    /// NOT IN THE EXPORT, and worth being explicit about. The graph
    /// says only "wait until the alien is far enough out, then go
    /// backstage" - it assumes the creature is already drifting away
    /// through backstage roaming, so it never needs a node that means
    /// "leave". Without backstage that assumption does not hold, and a
    /// withdrawal would consist of the creature standing next to you
    /// waiting to be far away.
    /// </summary>
    public static BTNode WithdrawMoveAway(
        float metres,
        Sst urgency)
    {
        bool issued = false;

        return new BTAction(
            "WithdrawMoveAway(" + metres + ")",

            c =>
            {
                if (issued &&
                    !c.Locomotion.HasArrived &&
                    !c.Locomotion.PathFailed)
                {
                    return BTStatus.Running;
                }

                issued = false;

                Vector3 from =
                    c.Perception.HasLastKnownPosition
                        ? c.Perception.LastKnownPosition
                        : c.Owner.position;

                Vector3 away = c.Owner.position - from;

                away.y = 0f;

                if (away.sqrMagnitude < 0.01f)
                {
                    away = c.Owner.forward;
                }

                away.Normalize();

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    /*
                     * Fan out from straight-away rather than picking at
                     * random, so the creature commits to a direction
                     * instead of dithering near the player.
                     */

                    float spread =
                        attempt == 0
                            ? 0f
                            : Random.Range(-70f, 70f);

                    Vector3 direction =
                        Quaternion.Euler(0f, spread, 0f) * away;

                    Vector3 candidate =
                        from + direction * metres;

                    if (!c.Locomotion.TrySnapToNavMesh(
                            candidate, out Vector3 snapped))
                    {
                        continue;
                    }

                    if (c.Locomotion.MoveTo(snapped, Gait.Walk))
                    {
                        issued = true;

                        return BTStatus.Running;
                    }
                }

                return BTStatus.Failure;
            },

            urgency,

            (c, u) =>
            {
                issued = false;

                if (u == Sst.Critical)
                {
                    c.Locomotion.Stop();
                }
            }
        );
    }


    /// <summary>
    /// Alien_HitAndRun - take the kill on the way out.
    ///
    /// Its own exported graph, not ported. Left failing so the
    /// withdrawal falls through to actually leaving, which is the
    /// behaviour the Director asked for.
    /// </summary>
    public static BTNode AlienHitAndRun()
    {
        return new BTNotImplemented("Alien_HitAndRun");
    }


    // =========================================================
    // SUSPICIOUS ITEMS
    // =========================================================

    /// <summary>SuspiciousItemInProgress.</summary>
    public static BTNode SuspiciousItemInProgress()
    {
        return new BTCondition(
            "SuspiciousItemInProgress",
            c =>
                c.SuspiciousItems != null &&
                c.SuspiciousItems.InProgress
        );
    }


    /// <summary>
    /// SuspiciousItemBTPriority(P).
    ///
    /// The three branches are identical apart from this. It is what
    /// makes the same subtree run at three different heights in the
    /// root selector, so a thrown decoy outranks searching for the
    /// player and a swinging door does not.
    /// </summary>
    public static BTNode SuspiciousItemBTPriority(
        SuspiciousItemPriority priority)
    {
        return new BTCondition(
            "SuspiciousItemBTPriority(" + priority + ")",
            c =>
                c.SuspiciousItems != null &&
                c.SuspiciousItems.InProgress &&
                c.SuspiciousItems.Priority == priority
        );
    }


    public static BTNode ShouldProcessSuspiciousItem()
    {
        return new BTCondition(
            "ShouldProcessSuspiciousItem",
            c =>
                c.SuspiciousItems != null &&
                c.SuspiciousItems.InProgress
        );
    }


    public static BTNode SuspiciousItemShouldDoStage(
        SuspiciousItemStage stage)
    {
        return new BTCondition(
            "SuspiciousItemShouldDoStage(" + stage + ")",
            c =>
                c.SuspiciousItems != null &&
                c.SuspiciousItems.ShouldDoStage(stage)
        );
    }


    public static BTNode SuspiciousItemDoneStage(
        SuspiciousItemStage stage)
    {
        return new BTAction(
            "SuspiciousItemDoneStage(" + stage + ")",
            c =>
            {
                c.SuspiciousItems?.DoneStage(stage);

                return BTStatus.Success;
            }
        );
    }


    public static BTNode SuspiciousItemIsWithinDistance(float metres)
    {
        return new BTCondition(
            "SuspiciousItemIsWithinDistance(" + metres + ")",
            c =>
                c.SuspiciousItems != null &&
                c.SuspiciousItems.IsWithinDistance(metres)
        );
    }


    /// <summary>
    /// SuspiciousItemMoveTo(SPEED, SST).
    ///
    /// The gait is chosen by the caller from how far away the item is -
    /// the export picks Fastest beyond 15m, Fast inside it, Slow inside
    /// 8m. The creature arrives at a decoy hard and then creeps the
    /// last few metres, which reads as eagerness turning into caution.
    /// </summary>
    public static BTNode SuspiciousItemMoveTo(
        Gait gait,
        Sst urgency)
    {
        bool issued = false;

        return new BTAction(
            "SuspiciousItemMoveTo(" + gait + ", " + urgency + ")",

            c =>
            {
                if (c.SuspiciousItems == null ||
                    !c.SuspiciousItems.InProgress)
                {
                    return BTStatus.Failure;
                }

                Vector3 target = c.SuspiciousItems.Position;

                if (c.Locomotion.PathFailed)
                    return BTStatus.Failure;

                if (!issued)
                {
                    if (!c.Locomotion.MoveTo(target, gait))
                        return BTStatus.Failure;

                    issued = true;
                }
                else if (c.Locomotion.CurrentGait != gait)
                {
                    c.Locomotion.SetGait(gait);
                }

                if (c.Locomotion.HasArrived)
                {
                    issued = false;

                    return BTStatus.Success;
                }

                return BTStatus.Running;
            },

            urgency,

            (c, u) =>
            {
                issued = false;

                if (u == Sst.Critical)
                {
                    c.Locomotion.Stop();
                }
            }
        );
    }


    /// <summary>
    /// SuspiciousItemReaction(KIND, SST) - stop and register the thing.
    /// </summary>
    public static BTNode SuspiciousItemReaction(
        string kind,
        float duration,
        Sst urgency)
    {
        float until = -1f;

        return new BTAction(
            "SuspiciousItemReaction(" + kind + ", " + urgency + ")",

            c =>
            {
                if (c.SuspiciousItems == null ||
                    !c.SuspiciousItems.InProgress)
                {
                    return BTStatus.Failure;
                }

                if (until < 0f)
                {
                    c.Locomotion.Stop();

                    until = Time.time + duration;
                }

                c.Locomotion.FaceTowards(c.SuspiciousItems.Position);

                c.Animation?.RequestLookAround(
                    Mathf.Max(0.05f, until - Time.time));

                if (Time.time < until)
                    return BTStatus.Running;

                until = -1f;

                return BTStatus.Success;
            },

            urgency,

            (c, u) =>
            {
                until = -1f;

                c.Animation?.CancelLookAround();

                c.Locomotion.ClearFacing();
            }
        );
    }


    /// <summary>
    /// IdleTimeFacingSuspiciousItem(seconds, 0, SST). A negative time
    /// means wait indefinitely, which the export uses while other group
    /// members are still routing in.
    /// </summary>
    public static BTNode IdleTimeFacingSuspiciousItem(
        float seconds,
        Sst urgency)
    {
        float until = -1f;

        return new BTAction(
            "IdleTimeFacingSuspiciousItem(" + seconds + ", 0, " +
            urgency + ")",

            c =>
            {
                if (c.SuspiciousItems == null ||
                    !c.SuspiciousItems.InProgress)
                {
                    return BTStatus.Failure;
                }

                if (until < 0f && seconds >= 0f)
                {
                    c.Locomotion.Stop();

                    until = Time.time + seconds;
                }

                c.Locomotion.FaceTowards(c.SuspiciousItems.Position);

                if (seconds < 0f || Time.time < until)
                    return BTStatus.Running;

                until = -1f;

                return BTStatus.Success;
            },

            urgency,

            (c, u) =>
            {
                until = -1f;

                c.Locomotion.ClearFacing();
            }
        );
    }


    /// <summary>
    /// IsPartOfNPCGroup - false, and structurally so.
    ///
    /// Group coordination is the one part of the suspicious item
    /// machinery that cannot exist with a single creature: it is about
    /// several aliens dividing one item between them. The stage that
    /// depends on it is wired to complete immediately rather than
    /// stubbed out, so the shape survives for a future second creature.
    /// </summary>
    public static BTNode IsPartOfNPCGroup()
    {
        return new BTCondition("IsPartOfNPCGroup", c => false);
    }


    public static BTNode SuspiciousItemFirstGroupMember()
    {
        return new BTCondition(
            "SuspiciousItemFirstGroupMember", c => true);
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private static float Flat(
        Vector3 a,
        Vector3 b)
    {
        Vector3 delta = a - b;

        delta.y = 0f;

        return delta.magnitude;
    }
}
