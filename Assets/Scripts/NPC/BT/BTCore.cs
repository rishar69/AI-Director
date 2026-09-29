using UnityEngine;

/// <summary>
/// What a node reports after a tick.
/// </summary>
public enum BTStatus
{
    Failure = 0,
    Success = 1,

    /// <summary>
    /// Still working. The tree will come back to this node next tick
    /// unless something higher priority takes over.
    /// </summary>
    Running = 2
}


/// <summary>
/// How urgently a running action should be cut off.
///
/// CATHODE passes this to every action leaf as SST_GRACEFULL or
/// SST_CRITICAL. Reading it as abort politeness is an inference, not
/// something the exported graphs state outright - but it is the only
/// reading that explains why a parameter like this would sit on an
/// action at all, and it is what makes a reactive tree survivable:
/// a graceful abort lets an animation finish, a critical one does not.
/// </summary>
public enum Sst
{
    Graceful = 0,
    Critical = 1
}


/// <summary>
/// Everything a node is allowed to reach while ticking.
///
/// Deliberately narrow. Nodes get the blackboard and the creature's own
/// components - never the player, and never the Director. The Director
/// influences the tree by setting motivations on the blackboard, which
/// is exactly the coupling the exported trees use.
/// </summary>
public class BTContext
{
    public XenoBlackboard Blackboard { get; private set; }

    public PerceptionSystem Perception { get; private set; }

    /// <summary>
    /// The eight stimulus channels. This is what branch conditions
    /// actually query - Perception's single awareness value is the
    /// older, coarser view and the tree does not use it.
    /// </summary>
    public XenoSenses Senses { get; private set; }

    public XenoLocomotion Locomotion { get; private set; }

    public XenoAnimatorDriver Animation { get; private set; }

    public Transform Owner { get; private set; }

    /// <summary>
    /// Bookkeeping for the suspicious item being processed, if any.
    /// May be null on a creature that has no such component - every
    /// leaf that touches it checks first, so the branches simply fail
    /// through.
    /// </summary>
    public XenoSuspiciousItems SuspiciousItems { get; set; }

    /// <summary>
    /// When the four competing stimuli last happened, for
    /// EventAOccuredAfterB. May be null; leaves check first.
    /// </summary>
    public XenoEventLog Events { get; set; }

    public float DeltaTime => Time.deltaTime;

    /// <summary>
    /// Urgency used when a reactive composite cuts off a running child
    /// it no longer wants.
    /// </summary>
    public Sst DefaultAbortUrgency { get; set; } = Sst.Graceful;


    public BTContext(
        XenoBlackboard blackboard,
        PerceptionSystem perception,
        XenoSenses senses,
        XenoLocomotion locomotion,
        XenoAnimatorDriver animation,
        Transform owner)
    {
        Blackboard = blackboard;
        Perception = perception;
        Senses = senses;
        Locomotion = locomotion;
        Animation = animation;
        Owner = owner;
    }
}


/// <summary>
/// Base class for every node in the tree.
/// </summary>
public abstract class BTNode
{
    /// <summary>Shown in the debug trace. Match the CATHODE label.</summary>
    public string Name { get; protected set; }

    public BTStatus LastStatus { get; private set; } = BTStatus.Failure;

    public bool IsRunning => LastStatus == BTStatus.Running;


    protected BTNode(string name)
    {
        Name = name;
    }


    public BTStatus Tick(BTContext context)
    {
        LastStatus = OnTick(context);

        return LastStatus;
    }


    /// <summary>
    /// Cuts this node off part way through.
    ///
    /// Only meaningful while running - a node that already finished has
    /// nothing to unwind.
    /// </summary>
    public void Abort(
        BTContext context,
        Sst urgency)
    {
        if (LastStatus != BTStatus.Running)
            return;

        OnAbort(context, urgency);

        LastStatus = BTStatus.Failure;
    }


    protected abstract BTStatus OnTick(BTContext context);


    protected virtual void OnAbort(
        BTContext context,
        Sst urgency)
    {
    }


    public override string ToString()
    {
        return Name;
    }
}
