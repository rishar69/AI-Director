using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The letterbox the Director posts hints through.
///
/// Lives on the creature and implements IXenoDirectorHints, which is
/// what XenoBrain looks for on its own GameObject. The Director never
/// touches the brain; it writes into this, and the brain reads it and
/// decides for itself what to do.
///
/// Everything the Director can say is here, and it is deliberately not
/// much: an area, and two moods. There is no SetDestination, no
/// SetState, and no way to inject a perception.
/// </summary>
public class DirectorHintReceiver : MonoBehaviour, IXenoDirectorHints
{
    // =========================================================
    // REGISTRY
    // =========================================================

    /*
     * A static registry rather than the Director hunting for receivers,
     * so creatures spawned mid-session are picked up with no ordering
     * problems.
     */

    private static readonly List<DirectorHintReceiver> active =
        new List<DirectorHintReceiver>();

    public static IReadOnlyList<DirectorHintReceiver> Active => active;


    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
    }


    // =========================================================
    // STATE
    // =========================================================

    [Header("Debug")]
    [SerializeField]
    private bool drawGizmos = true;


    public XenoBrain Brain { get; private set; }

    public PerceptionSystem Perception { get; private set; }

    public XenoLocomotion Locomotion { get; private set; }


    // =========================================================
    // IXenoDirectorHints
    // =========================================================

    public bool HasSearchHint { get; private set; }

    public Vector3 SearchHintPosition { get; private set; }

    public float SearchHintRadius { get; private set; }

    public bool ShouldRetreat { get; private set; }

    public bool ShouldCommit { get; private set; }


    /// <summary>
    /// Raised when the creature reports it has looked over the hinted
    /// area, so the Director can choose somewhere else.
    /// </summary>
    public event Action<DirectorHintReceiver> OnHintReached;


    public void NotifyHintReached()
    {
        HasSearchHint = false;

        OnHintReached?.Invoke(this);
    }


    // =========================================================
    // DIRECTOR FACING API
    // =========================================================

    public void SetHint(
        Vector3 position,
        float radius)
    {
        HasSearchHint = true;

        SearchHintPosition = position;

        SearchHintRadius = Mathf.Max(1f, radius);
    }


    public void ClearHint()
    {
        HasSearchHint = false;
    }


    public void SetRetreat(bool value)
    {
        ShouldRetreat = value;
    }


    public void SetCommit(bool value)
    {
        ShouldCommit = value;
    }


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        Brain = GetComponent<XenoBrain>();

        Perception = GetComponent<PerceptionSystem>();

        Locomotion = GetComponent<XenoLocomotion>();
    }


    private void OnEnable()
    {
        if (!active.Contains(this))
        {
            active.Add(this);
        }
    }


    private void OnDisable()
    {
        active.Remove(this);
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !HasSearchHint)
            return;

        Gizmos.color = new Color(0.2f, 1f, 0.9f, 0.85f);

        Gizmos.DrawWireSphere(
            SearchHintPosition,
            SearchHintRadius
        );

        Gizmos.DrawLine(
            transform.position + Vector3.up,
            SearchHintPosition
        );
    }
}
