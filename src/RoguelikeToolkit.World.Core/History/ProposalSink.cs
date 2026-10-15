using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Collects one operator's proposals for one turn and hands it the operator-scoped RNG streams and
/// budgets. The driver creates one sink per operator per turn.
/// </summary>
public sealed class ProposalSink
{
    private readonly HistoryContext _context;
    private readonly List<Proposal> _proposals;
    private readonly int _seed;

    internal ProposalSink(string operatorName, int turn, int seed, HistoryContext context, List<Proposal> proposals)
    {
        OperatorName = operatorName;
        Turn = turn;
        _seed = seed;
        _context = context;
        _proposals = proposals;
    }

    /// <summary>Name of the operator this sink belongs to.</summary>
    public string OperatorName { get; }

    /// <summary>The turn being simulated.</summary>
    public int Turn { get; }

    /// <summary>The operator's RNG stream for <paramref name="entityId"/> and <paramref name="purpose"/> this turn.</summary>
    public Rng Rng(int entityId, string purpose) => HistoryRng.Create(_seed, OperatorName, Turn, entityId, purpose);

    /// <summary>Spends one decision from this operator's budget for the turn; false when exhausted.</summary>
    public bool TrySpendDecision() => _context.TrySpendOperatorDecision(OperatorName, Turn);

    /// <summary>Spends one emergence from this operator's budget for the turn; false when exhausted.</summary>
    public bool TrySpendEmergence() => _context.TrySpendOperatorEmergence(OperatorName, Turn);

    /// <summary>Emits <paramref name="proposal"/>; it is applied at commit.</summary>
    public void Emit(Proposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        proposal.Operator = OperatorName;
        proposal.Sequence = _proposals.Count;
        _proposals.Add(proposal);
    }

    /// <summary>Emits a scalar delta.</summary>
    public void Adjust(int polityId, int scalarId, double delta)
        => Emit(new Proposal.AdjustScalar { EntityId = polityId, ScalarId = scalarId, Delta = delta });

    /// <summary>Emits an event append.</summary>
    public void Event(HistoryEventKind kind, int polityId, int tile, IReadOnlyList<string> tags, int cause = -1,
        IReadOnlyList<EventParticipant>? participants = null)
        => Emit(new Proposal.AppendEvent { EntityId = polityId, EventKind = kind, Tile = tile, Tags = tags, Cause = cause, Participants = participants });
}
