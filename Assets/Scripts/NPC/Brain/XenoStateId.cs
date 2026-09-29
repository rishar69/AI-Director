/// <summary>
/// Every behaviour the xenomorph can be in.
///
/// Ordered roughly by escalation, but nothing depends on the numeric
/// values - transitions are explicit, not threshold based, because a
/// creature that can only escalate one step at a time cannot break off
/// a chase to search, or drop from stalking straight back to patrol.
/// </summary>
public enum XenoStateId
{
    /// <summary>Roaming. Knows nothing.</summary>
    Patrol = 0,

    /// <summary>Walking to a specific spot something happened.</summary>
    Investigate = 1,

    /// <summary>Sweeping several points around a lost contact.</summary>
    Search = 2,

    /// <summary>
    /// Knows roughly where the prey is and is deliberately not
    /// attacking yet. The state that makes it feel like a xenomorph
    /// rather than a zombie.
    /// </summary>
    Stalk = 3,

    /// <summary>Committed. Running it down.</summary>
    Chase = 4,

    /// <summary>Striking.</summary>
    Attack = 5,

    /// <summary>Driven off. Breaking contact entirely.</summary>
    Retreat = 6
}
