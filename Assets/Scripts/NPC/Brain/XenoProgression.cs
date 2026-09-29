using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One step in the creature's apparent education.
/// </summary>
[Serializable]
public class UnlockStage
{
    [Tooltip("Shown in logs and the debug overlay.")]
    public string name = "Stage";

    [Tooltip("Minutes of play before this unlocks.")]
    [Min(0f)]
    public float atMinutes = 2f;

    [Header("Behaviour")]
    [Tooltip("Start checking hiding places during a search.")]
    public bool checkHidingSpots = false;

    [Tooltip("Sense through thin cover. A late, nasty unlock.")]
    public bool senseThroughCover = false;

    [Header("Multipliers On The Starting Values")]
    [Min(0.1f)]
    public float stalkDurationScale = 1f;

    [Min(0.1f)]
    public float hearingSensitivityScale = 1f;

    [Min(0.1f)]
    public float visionRangeScale = 1f;

    [Min(0.1f)]
    public float loseContactTimeScale = 1f;
}


/// <summary>
/// Makes the creature appear to learn.
///
/// It does not learn. Nothing here adapts to what the player actually
/// did - stages simply switch on over time. That is the same trick
/// Alien: Isolation uses, and it works because the player has no way to
/// distinguish "it worked out that I hide in lockers" from "the locker
/// checking node turned on at the twelve minute mark". The perceived
/// intelligence is entirely in the timing.
///
/// The honest reason to build it this way rather than with real
/// learning: staged unlocks are tunable, testable and cannot produce a
/// creature that has taught itself to be unfun.
/// </summary>
[RequireComponent(typeof(XenoBrain))]
public class XenoProgression : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Stages")]
    [SerializeField]
    private List<UnlockStage> stages = new List<UnlockStage>();

    [Header("Clock")]
    /// <summary>
    /// When false the clock only advances while the creature is
    /// actually a threat, so a player who leaves the game running does
    /// not come back to a fully evolved alien.
    /// </summary>
    [SerializeField]
    private bool countIdleTime = false;

    [Header("Debug")]
    [SerializeField]
    private bool logUnlocks = true;

    /// <summary>
    /// Skips the clock. Useful for testing a late stage without
    /// playing for twelve minutes to reach it.
    /// </summary>
    [SerializeField, Min(0f)]
    private float startAtMinutes = 0f;


    // =========================================================
    // STATE
    // =========================================================

    private XenoBrain brain;

    private HearingSense hearing;

    private VisionSense vision;

    private ProximitySense proximity;

    private float elapsed;

    private int appliedStages;

    /*
     * Baselines captured once at Awake, because stage multipliers are
     * relative to the creature's authored values. Multiplying against
     * the live value instead would compound every stage into the next.
     */

    private float baseStalkDuration;

    private float baseHearingSensitivity;

    private float baseVisionRange;

    private float baseLoseContactTime;


    public float ElapsedMinutes => elapsed / 60f;

    public int AppliedStages => appliedStages;

    public int TotalStages => stages != null ? stages.Count : 0;

    public string CurrentStageName =>
        appliedStages > 0 && appliedStages <= stages.Count
            ? stages[appliedStages - 1].name
            : "none";

    public event Action<UnlockStage> OnStageApplied;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        brain = GetComponent<XenoBrain>();

        hearing = GetComponentInChildren<HearingSense>();

        vision = GetComponentInChildren<VisionSense>();

        proximity = GetComponentInChildren<ProximitySense>();

        CaptureBaselines();

        elapsed = startAtMinutes * 60f;

        if (stages.Count == 0)
        {
            BuildDefaultStages();
        }
    }


    private void Start()
    {
        /*
         * Applied at Start rather than Awake so the creature begins in
         * whatever state startAtMinutes implies, including stage zero
         * having stripped abilities it is authored with.
         */

        ApplyDueStages(silent: true);
    }


    private void Update()
    {
        if (ShouldCount())
        {
            elapsed += Time.deltaTime;
        }

        ApplyDueStages(silent: false);
    }


    private bool ShouldCount()
    {
        if (countIdleTime)
            return true;

        /*
         * Anything past patrolling counts as the creature being part
         * of the player's evening.
         */

        return brain.CurrentState != XenoStateId.Patrol;
    }


    // =========================================================
    // BASELINES
    // =========================================================

    private void CaptureBaselines()
    {
        baseStalkDuration = brain.StalkDuration;

        baseLoseContactTime = brain.LoseContactTime;

        baseHearingSensitivity =
            hearing != null ? hearing.Sensitivity : 1f;

        baseVisionRange =
            vision != null ? vision.Range : 18f;
    }


    // =========================================================
    // STAGES
    // =========================================================

    private void ApplyDueStages(bool silent)
    {
        while (appliedStages < stages.Count &&
               elapsed >= stages[appliedStages].atMinutes * 60f)
        {
            UnlockStage stage = stages[appliedStages];

            Apply(stage);

            appliedStages++;

            if (logUnlocks && !silent)
            {
                Debug.Log(
                    "[XenoProgression] Unlocked '" + stage.name +
                    "' at " + ElapsedMinutes.ToString("F1") + " min",
                    this
                );
            }

            OnStageApplied?.Invoke(stage);
        }
    }


    private void Apply(UnlockStage stage)
    {
        brain.SetCheckHidingSpots(stage.checkHidingSpots);

        brain.SetStalkDuration(
            baseStalkDuration * stage.stalkDurationScale
        );

        brain.SetLoseContactTime(
            baseLoseContactTime * stage.loseContactTimeScale
        );

        if (hearing != null)
        {
            hearing.SetSensitivity(
                baseHearingSensitivity * stage.hearingSensitivityScale
            );
        }

        if (vision != null)
        {
            vision.SetRange(
                baseVisionRange * stage.visionRangeScale
            );
        }

        if (proximity != null)
        {
            proximity.SetRespectObstacles(!stage.senseThroughCover);
        }
    }


    /// <summary>
    /// A sensible curve for a session, used when nothing is authored.
    ///
    /// The shape matters more than the numbers: it should start
    /// noticeably dumber than the authored creature and end slightly
    /// sharper, so a player who survives an hour genuinely faces
    /// something worse than the one who died in five minutes.
    /// </summary>
    private void BuildDefaultStages()
    {
        stages.Add(new UnlockStage
        {
            name = "Naive",
            atMinutes = 0f,
            checkHidingSpots = false,
            senseThroughCover = false,
            stalkDurationScale = 1.4f,
            hearingSensitivityScale = 0.8f,
            visionRangeScale = 0.85f,
            loseContactTimeScale = 0.8f
        });

        stages.Add(new UnlockStage
        {
            name = "Wary",
            atMinutes = 3f,
            checkHidingSpots = false,
            senseThroughCover = false,
            stalkDurationScale = 1.1f,
            hearingSensitivityScale = 1f,
            visionRangeScale = 1f,
            loseContactTimeScale = 1f
        });

        stages.Add(new UnlockStage
        {
            name = "Checks hiding places",
            atMinutes = 7f,
            checkHidingSpots = true,
            senseThroughCover = false,
            stalkDurationScale = 0.9f,
            hearingSensitivityScale = 1.15f,
            visionRangeScale = 1.05f,
            loseContactTimeScale = 1.2f
        });

        stages.Add(new UnlockStage
        {
            name = "Relentless",
            atMinutes = 14f,
            checkHidingSpots = true,
            senseThroughCover = true,
            stalkDurationScale = 0.6f,
            hearingSensitivityScale = 1.3f,
            visionRangeScale = 1.15f,
            loseContactTimeScale = 1.5f
        });
    }


    // =========================================================
    // API
    // =========================================================

    /// <summary>
    /// Rolls the creature back to knowing nothing. For a new run.
    /// </summary>
    public void ResetProgression()
    {
        elapsed = 0f;

        appliedStages = 0;

        ApplyDueStages(silent: true);
    }
}
