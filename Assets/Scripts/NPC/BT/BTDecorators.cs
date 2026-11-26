using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds a named timer alive for as long as its subtree is running.
///
/// The exports nest these - ATTACK_BRANCH opens with
/// Timer(30, SUSPECT_TARGET_RESPONSE_DELAY_TIMER) wrapping
/// Timer(25, HEIGHTENED_SENSES_TIMER) wrapping everything else.
///
/// It is a COOLDOWN SCOPE: when the subtree stops running, the named
/// timer is put on the clock for n seconds. Nothing happens while it
/// runs.
///
/// That reading is forced by alien_suspect_target_response, which sits
/// inside Timer(30, SUSPECT_TARGET_RESPONSE_DELAY_TIMER) and then, a
/// few nodes down, requires HasTimerExpired on that same timer. If the
/// wrapper held the timer down while running - my first reading - the
/// branch would block itself on its own first tick and could never do
/// anything. Starting the clock on exit makes both nodes coherent, and
/// gives the behaviour a natural "not again for thirty seconds".
/// </summary>
public class BTTimerScope : BTNode
{
    private readonly float seconds;

    private readonly TimerId timer;

    private readonly BTNode child;


    public BTTimerScope(
        float seconds,
        TimerId timer,
        BTNode child)
        : base("Timer(" + seconds + ", " + timer + ")")
    {
        this.seconds = seconds;

        this.timer = timer;

        this.child = child;
    }


    public BTNode Child => child;

    private BTNode[] traceChildren;

    public override System.Collections.Generic.IReadOnlyList<BTNode> TraceChildren =>
        traceChildren ??= (child != null ? new[] { child } : new BTNode[0]);


    private bool wasRunning;


    protected override BTStatus OnTick(BTContext context)
    {
        if (child == null)
            return BTStatus.Failure;

        BTStatus status = child.Tick(context);

        if (status == BTStatus.Running)
        {
            wasRunning = true;

            return status;
        }

        /*
         * Finished, so start the cooldown. Only on the transition -
         * re-arming it every tick the subtree is idle would keep it
         * permanently unexpired.
         */

        if (wasRunning)
        {
            wasRunning = false;

            context.Blackboard.StartTimer(seconds, timer);
        }

        return status;
    }


    protected override void OnAbort(
        BTContext context,
        Sst urgency)
    {
        child?.Abort(context, urgency);

        /*
         * Being cut off still counts as having run.
         */

        if (wasRunning)
        {
            wasRunning = false;

            context.Blackboard.StartTimer(seconds, timer);
        }
    }
}


/// <summary>
/// Picks one child at random by weight, then commits to it.
///
/// From PercentageSelector with Percentage(20) / Percentage(80)
/// children - the exports use it to decide once whether a flanked vent
/// attack is allowed, rather than re-rolling every tick.
/// </summary>
public class BTPercentageSelector : BTComposite
{
    private readonly List<float> weights = new List<float>();

    private int chosen = -1;


    public BTPercentageSelector(string name)
        : base(name)
    {
    }


    public BTPercentageSelector AddWeighted(
        float percent,
        BTNode child)
    {
        if (child == null)
            return this;

        Add(child);

        weights.Add(Mathf.Max(0f, percent));

        return this;
    }


    protected override BTStatus OnTick(BTContext context)
    {
        if (children.Count == 0)
            return BTStatus.Failure;

        if (chosen < 0)
        {
            chosen = Roll();
        }

        BTStatus status = children[chosen].Tick(context);

        if (status == BTStatus.Running)
        {
            runningIndex = chosen;

            return status;
        }

        /*
         * Re-rolled next time it is entered, not next tick - that is
         * what "decide this once at start of behaviour" means.
         */

        chosen = -1;

        runningIndex = -1;

        return status;
    }


    private int Roll()
    {
        float total = 0f;

        for (int i = 0; i < weights.Count; i++)
        {
            total += weights[i];
        }

        if (total <= 0f)
            return 0;

        float roll = Random.Range(0f, total);

        float running = 0f;

        for (int i = 0; i < weights.Count; i++)
        {
            running += weights[i];

            if (roll <= running)
                return i;
        }

        return weights.Count - 1;
    }


    protected override void OnAbort(
        BTContext context,
        Sst urgency)
    {
        base.OnAbort(context, urgency);

        chosen = -1;
    }
}


/// <summary>
/// Runs its child and always reports Failure.
///
/// For publish-only subtrees. A frame flag is set deep in one branch
/// so a LOWER priority branch can read it on the same tick - which
/// only works if the publisher does not halt the selector on its way
/// past. Success there would stop traversal before the branch that
/// was supposed to read the flag ever ran, and the flag is cleared
/// again at the top of the next tick, so it would never be seen at
/// all.
/// </summary>
public class BTAlwaysFail : BTNode
{
    private readonly BTNode child;


    public BTAlwaysFail(BTNode child)
        : base("publish:" + (child != null ? child.Name : "null"))
    {
        this.child = child;
    }

    private BTNode[] traceChildren;

    public override System.Collections.Generic.IReadOnlyList<BTNode> TraceChildren =>
        traceChildren ??= (child != null ? new[] { child } : new BTNode[0]);



    protected override BTStatus OnTick(BTContext context)
    {
        if (child == null)
            return BTStatus.Failure;

        /*
         * Running is passed through - a publisher that needed several
         * ticks would otherwise be restarted forever.
         */

        BTStatus status = child.Tick(context);

        return status == BTStatus.Running
            ? BTStatus.Running
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
/// Flips Success and Failure.
///
/// The graphs express negation by wiring a condition's False output
/// onward instead of its True output, which has no direct textual form.
/// This is the closest node shape to that.
/// </summary>
public class BTInverter : BTNode
{
    private readonly BTNode child;


    public BTInverter(BTNode child)
        : base("not " + (child != null ? child.Name : "null"))
    {
        this.child = child;
    }

    private BTNode[] traceChildren;

    public override System.Collections.Generic.IReadOnlyList<BTNode> TraceChildren =>
        traceChildren ??= (child != null ? new[] { child } : new BTNode[0]);



    protected override BTStatus OnTick(BTContext context)
    {
        if (child == null)
            return BTStatus.Failure;

        BTStatus status = child.Tick(context);

        if (status == BTStatus.Success) return BTStatus.Failure;

        if (status == BTStatus.Failure) return BTStatus.Success;

        return status;
    }


    protected override void OnAbort(
        BTContext context,
        Sst urgency)
    {
        child?.Abort(context, urgency);
    }
}
