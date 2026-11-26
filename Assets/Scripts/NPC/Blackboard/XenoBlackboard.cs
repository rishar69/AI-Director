using System;
using UnityEngine;

/// <summary>
/// Everything the behaviour tree reads and writes that is not a
/// component.
///
/// Motivations, flags, gauges, timers, roles and withdraw state - the
/// supporting systems the exported trees lean on constantly. Kept as
/// one component because they are queried together dozens of times per
/// tick and because it gives the debug overlay a single place to look.
///
/// This is also the Director's only writable surface. It may raise a
/// motivation or push a gauge; it may not set a destination, force a
/// branch, or hand over the player's position.
/// </summary>
[DisallowMultipleComponent]
public class XenoBlackboard : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Gauge Thresholds")]
    /*
     * Gauges are stored as a continuous 0..1 amount but always read
     * through these levels, exactly as IsGaugeAmountAbove does.
     */

    [SerializeField, Range(0f, 1f)]
    private float traceThreshold = 0.15f;

    [SerializeField, Range(0f, 1f)]
    private float lowerThreshold = 0.40f;

    [SerializeField, Range(0f, 1f)]
    private float activatedThreshold = 0.75f;


    [Header("Debug")]
    [SerializeField]
    private bool logMotivationChanges = true;

    [SerializeField]
    private bool logFlagChanges = false;


    // =========================================================
    // STATE
    // =========================================================

    private readonly bool[] motivations =
        new bool[Enum.GetValues(typeof(Motivation)).Length];

    private readonly bool[] logicFlags =
        new bool[Enum.GetValues(typeof(LogicFlag)).Length];

    private readonly bool[] targetFlags =
        new bool[Enum.GetValues(typeof(TargetLogicFlag)).Length];

    private readonly bool[] frameFlags =
        new bool[Enum.GetValues(typeof(FrameFlag)).Length];

    private readonly float[] gauges =
        new float[Enum.GetValues(typeof(GaugeId)).Length];

    /*
     * Timer end times in Time.time. A negative value means the timer
     * has never been started.
     */

    private readonly float[] timerEnds =
        new float[Enum.GetValues(typeof(TimerId)).Length];


    private readonly bool[] permissions =
        new bool[Enum.GetValues(typeof(AlienPermission)).Length];


    public Role CurrentRole { get; private set; } = Role.None;

    public WithdrawState Withdraw { get; private set; } =
        WithdrawState.NotWithdrawing;


    public event Action<Motivation, bool> OnMotivationChanged;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        for (int i = 0; i < timerEnds.Length; i++)
        {
            timerEnds[i] = -1f;
        }

        /*
         * Permissions default to granted. The Director revokes them;
         * nothing has to remember to hand them out.
         */

        for (int i = 0; i < permissions.Length; i++)
        {
            permissions[i] = true;
        }
    }


    // =========================================================
    // PERMISSIONS
    // =========================================================

    public bool AlienIsAllowed(AlienPermission permission)
    {
        return permissions[(int)permission];
    }


    public void SetAllowed(
        AlienPermission permission,
        bool value)
    {
        permissions[(int)permission] = value;
    }


    /// <summary>
    /// Called by the brain at the top of every tick, before the tree
    /// runs. Frame flags exist only for the tick that set them.
    /// </summary>
    public void BeginTick()
    {
        Array.Clear(frameFlags, 0, frameFlags.Length);
    }


    // =========================================================
    // MOTIVATIONS
    // =========================================================

    public bool HasMotivation(Motivation motivation)
    {
        return motivations[(int)motivation];
    }


    public void SetMotivation(
        Motivation motivation,
        bool value)
    {
        int index = (int)motivation;

        if (motivations[index] == value)
            return;

        motivations[index] = value;

        if (logMotivationChanges)
        {
            Debug.Log(
                "[Blackboard] motivation " + motivation +
                (value ? " raised" : " cleared"),
                this
            );
        }

        OnMotivationChanged?.Invoke(motivation, value);
    }


    public void ClearAllMotivations()
    {
        for (int i = 0; i < motivations.Length; i++)
        {
            SetMotivation((Motivation)i, false);
        }
    }


    /// <summary>
    /// Highest priority live motivation, for the debug overlay. Uses
    /// enum order, which mirrors the root selector's child order.
    /// </summary>
    public string DescribeMotivations()
    {
        string result = null;

        for (int i = 0; i < motivations.Length; i++)
        {
            if (!motivations[i])
                continue;

            result = result == null
                ? ((Motivation)i).ToString()
                : result + ", " + (Motivation)i;
        }

        return result ?? "none";
    }


    // =========================================================
    // FLAGS
    // =========================================================

    public bool GetFlag(LogicFlag flag)
    {
        return logicFlags[(int)flag];
    }


    public void SetFlag(
        LogicFlag flag,
        bool value)
    {
        int index = (int)flag;

        if (logicFlags[index] == value)
            return;

        logicFlags[index] = value;

        if (logFlagChanges)
        {
            Debug.Log(
                "[Blackboard] flag " + flag + " = " + value,
                this
            );
        }
    }


    // =========================================================
    // TARGET WEAPON
    // =========================================================

    /*
     * Published by the player's own equipment, never read across by the
     * creature. Same arrangement as PLAYER_HIDING: the thing that
     * legitimately knows pushes, and the tree only ever sees the
     * blackboard.
     */

    /// <summary>
    /// Whether the target carries something with the
    /// ALIEN_THREAT_AWARE_OF property.
    /// </summary>
    public bool TargetWeaponIsThreat { get; private set; }

    public bool TargetWeaponHasAmmo { get; private set; }

    /// <summary>Where the target is currently aiming.</summary>
    public Vector3 TargetAimDirection { get; private set; } =
        Vector3.forward;


    public void SetTargetWeapon(
        bool isThreat,
        bool hasAmmo,
        Vector3 aimDirection)
    {
        TargetWeaponIsThreat = isThreat;

        TargetWeaponHasAmmo = hasAmmo;

        if (aimDirection.sqrMagnitude > 0.001f)
        {
            TargetAimDirection = aimDirection.normalized;
        }
    }


    public bool GetTargetFlag(TargetLogicFlag flag)
    {
        return targetFlags[(int)flag];
    }


    /// <summary>
    /// Written by whatever legitimately observes the target - the
    /// player's own hiding component publishes PLAYER_HIDING here
    /// rather than the tree reaching across to read it.
    /// </summary>
    public void SetTargetFlag(
        TargetLogicFlag flag,
        bool value)
    {
        targetFlags[(int)flag] = value;
    }


    public bool GetFrameFlag(FrameFlag flag)
    {
        return frameFlags[(int)flag];
    }


    public void SetFrameFlag(FrameFlag flag)
    {
        frameFlags[(int)flag] = true;
    }


    // =========================================================
    // SUSPECT RESPONSE
    // =========================================================

    /*
     * HasDoneSuspectResponseMoveTo and HasDoneSuspectResponseWithinTime
     * are read by the suspect-response branch to stop the creature
     * walking to the same noise over and over. They record the MOVE TO,
     * not the in-place reaction - the two are separate behaviours in
     * the export and only the move-to is rationed.
     */

    public bool HasDoneSuspectResponseMoveTo { get; private set; }

    private float lastSuspectResponseMoveToTime = -999f;


    public bool HasDoneSuspectResponseWithinTime(float seconds)
    {
        if (!HasDoneSuspectResponseMoveTo)
            return false;

        return Time.time - lastSuspectResponseMoveToTime <= seconds;
    }


    /// <summary>
    /// Called when a SUSPECT_RESPONSE_MOVE_TO role actually completes.
    /// Nothing calls this yet - the role lives in the search branch,
    /// which is still stubbed.
    /// </summary>
    public void NoteSuspectResponseMoveTo()
    {
        HasDoneSuspectResponseMoveTo = true;

        lastSuspectResponseMoveToTime = Time.time;
    }


    // =========================================================
    // GAUGES
    // =========================================================

    public float GetGaugeAmount(GaugeId gauge)
    {
        return gauges[(int)gauge];
    }


    public GaugeLevel GetGaugeLevel(GaugeId gauge)
    {
        float amount = gauges[(int)gauge];

        if (amount >= activatedThreshold) return GaugeLevel.Activated;

        if (amount >= lowerThreshold) return GaugeLevel.Lower;

        if (amount >= traceThreshold) return GaugeLevel.Trace;

        return GaugeLevel.None;
    }


    public bool IsGaugeAmountAbove(
        GaugeId gauge,
        GaugeLevel level)
    {
        return GetGaugeLevel(gauge) >= level;
    }


    public void AddGaugeAmount(
        GaugeId gauge,
        float delta)
    {
        int index = (int)gauge;

        gauges[index] = Mathf.Clamp01(gauges[index] + delta);
    }


    /// <summary>
    /// Sets a gauge to an exact amount.
    ///
    /// The Director drives the retreat gauge continuously, so it needs
    /// a real number rather than a level band - the tree still only
    /// ever READS it through the bands, which is what keeps the
    /// exported conditions honest.
    /// </summary>
    public void SetGaugeAmount(
        GaugeId gauge,
        float amount)
    {
        gauges[(int)gauge] = Mathf.Clamp01(amount);
    }


    /// <summary>
    /// Snaps a gauge to the bottom of a level band. Mirrors
    /// SetGaugeAmount(GAUGE, GAUGE_NONE).
    /// </summary>
    public void SetGaugeAmount(
        GaugeId gauge,
        GaugeLevel level)
    {
        gauges[(int)gauge] = AmountFor(level);
    }


    private float AmountFor(GaugeLevel level)
    {
        switch (level)
        {
            case GaugeLevel.Activated: return activatedThreshold;

            case GaugeLevel.Lower: return lowerThreshold;

            case GaugeLevel.Trace: return traceThreshold;

            default: return 0f;
        }
    }


    // =========================================================
    // TIMERS
    // =========================================================

    /// <summary>
    /// A duration of -1 means "run until something expires it", which
    /// is how the exported trees start open ended timers such as
    /// BACKSTAGE_STALK_TIMEOUT_TIMER.
    /// </summary>
    public void StartTimer(
        float seconds,
        TimerId timer)
    {
        timerEnds[(int)timer] =
            seconds < 0f
                ? float.MaxValue
                : Time.time + seconds;
    }


    public void StartTimerRandom(
        float minSeconds,
        float maxSeconds,
        TimerId timer)
    {
        if (minSeconds < 0f || maxSeconds < 0f)
        {
            StartTimer(-1f, timer);

            return;
        }

        StartTimer(
            UnityEngine.Random.Range(minSeconds, maxSeconds),
            timer
        );
    }


    public void ExpireTimer(TimerId timer)
    {
        timerEnds[(int)timer] = -1f;
    }


    /// <summary>
    /// True when the timer has run out, and also when it was never
    /// started.
    ///
    /// The default matters: a gate like
    /// HasTimerExpired(ATTACK_BAN_TIMER) has to pass on a fresh
    /// creature that has never been banned from attacking, or it could
    /// never attack at all.
    /// </summary>
    public bool HasTimerExpired(TimerId timer)
    {
        float end = timerEnds[(int)timer];

        if (end < 0f)
            return true;

        return Time.time >= end;
    }


    public bool IsTimerRunning(TimerId timer)
    {
        return !HasTimerExpired(timer);
    }


    public float TimeRemaining(TimerId timer)
    {
        float end = timerEnds[(int)timer];

        if (end < 0f)
            return 0f;

        if (end == float.MaxValue)
            return float.PositiveInfinity;

        return Mathf.Max(0f, end - Time.time);
    }


    // =========================================================
    // ROLE & WITHDRAW
    // =========================================================

    public bool IsPerformingRoleOrCouldPerformRole(Role role)
    {
        return CurrentRole == Role.None || CurrentRole == role;
    }


    public void SetRole(Role role)
    {
        CurrentRole = role;
    }


    public void SetWithdrawState(WithdrawState state)
    {
        Withdraw = state;
    }


    // =========================================================
    // DIRECTOR AREA (designed tree's macro lever)
    // =========================================================

    /// <summary>
    /// A coarse region the Director wants the creature to roam in. A
    /// REGION, never a point: the Director keeps its centre a set
    /// distance from the player and jitters it, so the creature still
    /// has to find the player with its own senses once it gets there.
    /// Only the ROAM branch reads it - the hunt, the chase and every
    /// other branch ignore it entirely.
    /// </summary>
    public DirectorIntent DirectorIntent { get; private set; } = DirectorIntent.None;

    public Vector3 DirectorAreaCenter { get; private set; }

    public float DirectorAreaRadius { get; private set; }

    public bool HasDirectorArea => DirectorIntent != DirectorIntent.None;


    public void SetDirectorArea(Vector3 center, float radius, DirectorIntent intent)
    {
        DirectorAreaCenter = center;
        DirectorAreaRadius = radius;
        DirectorIntent = intent;
    }


    public void ClearDirectorArea()
    {
        DirectorIntent = DirectorIntent.None;
    }


    // =========================================================
    // RESET
    // =========================================================

    /// <summary>
    /// Back to a creature that knows nothing. Used after a kill and by
    /// the Director when it relocates the creature.
    /// </summary>
    public void ResetAll()
    {
        Array.Clear(motivations, 0, motivations.Length);
        Array.Clear(logicFlags, 0, logicFlags.Length);
        Array.Clear(targetFlags, 0, targetFlags.Length);
        Array.Clear(frameFlags, 0, frameFlags.Length);
        Array.Clear(gauges, 0, gauges.Length);

        for (int i = 0; i < timerEnds.Length; i++)
        {
            timerEnds[i] = -1f;
        }

        CurrentRole = Role.None;

        Withdraw = WithdrawState.NotWithdrawing;

        HasDoneSuspectResponseMoveTo = false;

        lastSuspectResponseMoveToTime = -999f;

        DirectorIntent = DirectorIntent.None;
    }
}
