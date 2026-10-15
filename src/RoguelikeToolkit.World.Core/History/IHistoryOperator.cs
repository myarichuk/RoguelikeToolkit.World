namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One history rule evaluated every turn against the read-only <see cref="TurnView"/> of the previous
/// turn's end state. It emits <see cref="Proposal"/>s through the sink and never mutates the world, so
/// outcomes cannot depend on registration order.
/// </summary>
public interface IHistoryOperator
{
    /// <summary>Unique name; keys the operator's RNG streams, budgets and commit tie-break.</summary>
    string Name { get; }

    /// <summary>Evaluation order among operators; affects only when <c>Propose</c> runs, never outcomes.</summary>
    int Order { get; }

    /// <summary>Reads <paramref name="view"/> and emits this turn's proposals into <paramref name="sink"/>.</summary>
    void Propose(TurnView view, ProposalSink sink);
}
