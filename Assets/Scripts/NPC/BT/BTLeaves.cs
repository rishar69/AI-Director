using System;
using System.Text;

/// <summary>
/// A test. Succeeds or fails in one tick, never runs.
/// </summary>
public class BTCondition : BTNode
{
    private readonly Func<BTContext, bool> test;


    public BTCondition(
        string name,
        Func<BTContext, bool> test)
        : base(name)
    {
        this.test = test;
    }


    protected override BTStatus OnTick(BTContext context)
    {
        return test != null && test(context)
            ? BTStatus.Success
            : BTStatus.Failure;
    }
}


/// <summary>
/// Something the creature does. May take several ticks.
///
/// Carries the Sst urgency from its CATHODE label so an abort handler
/// can tell a polite interruption from an immediate one.
/// </summary>
public class BTAction : BTNode
{
    private readonly Func<BTContext, BTStatus> run;

    private readonly Action<BTContext, Sst> abort;

    public Sst Urgency { get; private set; }


    public BTAction(
        string name,
        Func<BTContext, BTStatus> run,
        Sst urgency = Sst.Graceful,
        Action<BTContext, Sst> abort = null)
        : base(name)
    {
        this.run = run;

        this.abort = abort;

        Urgency = urgency;
    }


    protected override BTStatus OnTick(BTContext context)
    {
        return run != null
            ? run(context)
            : BTStatus.Failure;
    }


    protected override void OnAbort(
        BTContext context,
        Sst urgency)
    {
        abort?.Invoke(context, urgency);
    }
}


/// <summary>
/// A named subtree.
///
/// Carries no behaviour of its own - it exists so the trace reads the
/// way the exported graphs do, where every top level child of the root
/// selector is a Branch(SOME_NAME).
/// </summary>
public class BTBranch : BTNode
{
    private readonly BTNode child;


    public BTBranch(
        string name,
        BTNode child)
        : base(name)
    {
        this.child = child;
    }


    public BTNode Child => child;

    private BTNode[] traceChildren;

    public override System.Collections.Generic.IReadOnlyList<BTNode> TraceChildren =>
        traceChildren ??= (child != null ? new[] { child } : new BTNode[0]);


    protected override BTStatus OnTick(BTContext context)
    {
        return child != null
            ? child.Tick(context)
            : BTStatus.Failure;
    }


    protected override void OnAbort(
        BTContext context,
        Sst urgency)
    {
        child?.Abort(context, urgency);
    }
}


/// <summary>
/// Always fails. Stands in for a branch that has not been ported yet.
///
/// Failing rather than succeeding is the safe choice: an unported
/// branch simply falls through to the next priority, so the tree keeps
/// working while it is incomplete.
/// </summary>
public class BTNotImplemented : BTNode
{
    public BTNotImplemented(string name)
        : base("(stub) " + name)
    {
    }


    protected override BTStatus OnTick(BTContext context)
    {
        return BTStatus.Failure;
    }
}


/// <summary>
/// Walks a tree and writes the path that was active on the last tick.
/// Used by the debug overlay.
/// </summary>
public static class BTTrace
{
    public static void Describe(
        BTNode node,
        StringBuilder builder,
        int depth = 0,
        int maxDepth = 6)
    {
        if (node == null || depth > maxDepth)
            return;

        /*
         * Only the live path is interesting. Printing every node would
         * bury the two or three that actually ran.
         */

        if (node.LastStatus != BTStatus.Running &&
            node.LastStatus != BTStatus.Success)
        {
            return;
        }

        builder.Append(' ', depth * 2);

        builder.Append(
            node.LastStatus == BTStatus.Running ? "> " : "  "
        );

        builder.AppendLine(node.Name);

        if (node is BTBranch branch)
        {
            Describe(branch.Child, builder, depth + 1, maxDepth);

            return;
        }

        if (node is BTComposite composite)
        {
            foreach (BTNode child in composite.Children)
            {
                Describe(child, builder, depth + 1, maxDepth);
            }
        }
    }
}
