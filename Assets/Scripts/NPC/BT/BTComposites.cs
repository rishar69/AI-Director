using System.Collections.Generic;

/// <summary>
/// A node with children.
/// </summary>
public abstract class BTComposite : BTNode
{
    protected readonly List<BTNode> children = new List<BTNode>();

    protected int runningIndex = -1;


    protected BTComposite(string name) : base(name)
    {
    }


    public BTComposite Add(BTNode child)
    {
        if (child != null)
        {
            children.Add(child);
        }

        return this;
    }


    public BTComposite AddRange(params BTNode[] range)
    {
        foreach (BTNode child in range)
        {
            Add(child);
        }

        return this;
    }


    public IReadOnlyList<BTNode> Children => children;

    public override IReadOnlyList<BTNode> TraceChildren => children;


    protected override void OnAbort(
        BTContext context,
        Sst urgency)
    {
        if (runningIndex >= 0 && runningIndex < children.Count)
        {
            children[runningIndex].Abort(context, urgency);
        }

        runningIndex = -1;
    }


    protected void AbortRunningChild(
        BTContext context,
        int except)
    {
        if (runningIndex < 0 || runningIndex == except)
            return;

        if (runningIndex < children.Count)
        {
            children[runningIndex].Abort(
                context,
                context.DefaultAbortUrgency
            );
        }
    }
}


/// <summary>
/// Runs children in order until one succeeds. Priority arbitration.
///
/// REACTIVE BY DEFAULT, and that matters more than it looks. The root
/// of alien_behave is a Selector whose children are guarded by
/// HasMotivation(...). If it were not reactive, a low priority branch
/// that returned Running would keep the tree hostage and a newly raised
/// motivation could not take over until that branch finished on its
/// own - which would make the entire motivation system inert.
///
/// Reactive means every tick starts again from child zero, so a higher
/// priority guard that has just become true wins immediately, and the
/// child that was running gets aborted.
/// </summary>
public class BTSelector : BTComposite
{
    private readonly bool reactive;


    public BTSelector(
        string name,
        bool reactive = true)
        : base(name)
    {
        this.reactive = reactive;
    }


    protected override BTStatus OnTick(BTContext context)
    {
        int start =
            (reactive || runningIndex < 0)
                ? 0
                : runningIndex;

        for (int i = start; i < children.Count; i++)
        {
            BTStatus status = children[i].Tick(context);

            if (status == BTStatus.Failure)
                continue;

            /*
             * Something other than the previously running child has
             * taken over, so unwind the old one before committing.
             */

            AbortRunningChild(context, i);

            runningIndex =
                status == BTStatus.Running ? i : -1;

            return status;
        }

        AbortRunningChild(context, -1);

        runningIndex = -1;

        return BTStatus.Failure;
    }
}


/// <summary>
/// Runs children in order until one fails.
///
/// Two axes, both taken from the CATHODE labels:
///
/// - CHILD_DEFAULT vs IGNORE_CHILD_FAIL, which is whether a failing
///   child stops the sequence or is simply skipped.
///
/// - Stateful vs stateless. A plain Sequence remembers which child was
///   running and resumes there. SequenceStateless restarts from the
///   first child every tick, so the conditions in front of a running
///   action are re-checked continuously - which is how a guard like
///   "keep checking gauge" in the stun branch keeps its grip on an
///   action that is already under way.
/// </summary>
public class BTSequence : BTComposite
{
    public enum FailPolicy
    {
        /// <summary>A failing child fails the sequence.</summary>
        Default = 0,

        /// <summary>A failing child is skipped.</summary>
        IgnoreChildFail = 1
    }


    private readonly FailPolicy policy;

    private readonly bool stateless;


    public BTSequence(
        string name,
        FailPolicy policy = FailPolicy.Default,
        bool stateless = false)
        : base(name)
    {
        this.policy = policy;

        this.stateless = stateless;
    }


    protected override BTStatus OnTick(BTContext context)
    {
        int start =
            (stateless || runningIndex < 0)
                ? 0
                : runningIndex;

        for (int i = start; i < children.Count; i++)
        {
            BTStatus status = children[i].Tick(context);

            if (status == BTStatus.Running)
            {
                AbortRunningChild(context, i);

                runningIndex = i;

                return BTStatus.Running;
            }

            if (status == BTStatus.Failure)
            {
                if (policy == FailPolicy.IgnoreChildFail)
                    continue;

                /*
                 * A stateless sequence can fail on a guard that sits
                 * in front of an action already running further down.
                 * That action has to be unwound.
                 */

                AbortRunningChild(context, -1);

                runningIndex = -1;

                return BTStatus.Failure;
            }
        }

        AbortRunningChild(context, -1);

        runningIndex = -1;

        return BTStatus.Success;
    }
}


/// <summary>
/// Convenience for the SequenceStateless(CHILD_DEFAULT) label.
/// </summary>
public class BTSequenceStateless : BTSequence
{
    public BTSequenceStateless(
        string name,
        FailPolicy policy = FailPolicy.Default)
        : base(name, policy, stateless: true)
    {
    }
}
