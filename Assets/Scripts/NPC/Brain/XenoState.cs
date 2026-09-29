using UnityEngine;

/// <summary>
/// Base class for one xenomorph behaviour.
///
/// Plain C# objects rather than MonoBehaviours. States are created once
/// by the brain and reused, so there is no allocation on transition,
/// and all tuning lives in one inspector on XenoBrain instead of being
/// scattered across seven components.
///
/// A state reaches the world only through Brain. It has no access to
/// the player, and gets its picture of the world exclusively from
/// PerceptionSystem.
/// </summary>
public abstract class XenoState
{
    private float enteredAt;


    protected XenoBrain Brain { get; private set; }

    protected PerceptionSystem Perception => Brain.Perception;

    protected XenoLocomotion Locomotion => Brain.Locomotion;

    protected XenoAnimatorDriver Animation => Brain.Animation;

    /// <summary>Seconds since this state was entered.</summary>
    protected float TimeInState => Time.time - enteredAt;


    public abstract XenoStateId Id { get; }


    // =========================================================
    // LIFECYCLE
    // =========================================================

    internal void Bind(XenoBrain brain)
    {
        Brain = brain;
    }


    internal void Enter()
    {
        enteredAt = Time.time;

        OnEnter();
    }


    internal void Exit()
    {
        OnExit();
    }


    /// <summary>
    /// Returns the state to move to, or null to stay put.
    /// </summary>
    internal XenoStateId? Tick()
    {
        return OnUpdate();
    }


    protected virtual void OnEnter()
    {
    }


    protected virtual void OnExit()
    {
    }


    protected abstract XenoStateId? OnUpdate();
}
