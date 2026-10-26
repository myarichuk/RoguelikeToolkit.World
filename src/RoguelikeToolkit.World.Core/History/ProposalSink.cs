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
    private readonly StringTable _strings;
    private readonly int _seed;
    private int _pendingPersons;

    internal ProposalSink(string operatorName, int turn, int seed, HistoryContext context, List<Proposal> proposals, StringTable? strings = null)
    {
        OperatorName = operatorName;
        Turn = turn;
        _seed = seed;
        _context = context;
        _proposals = proposals;
        _strings = strings ?? new StringTable();
    }

    /// <summary>The expansion features this run has on (see <see cref="HistoryContext.Features"/>).</summary>
    public HistoryFeatures Features => _context.Features;

    /// <summary>True when every flag of <paramref name="features"/> is on.</summary>
    public bool Has(HistoryFeatures features) => _context.Has(features);

    /// <summary>Name of the operator this sink belongs to.</summary>
    public string OperatorName { get; }

    /// <summary>The turn being simulated.</summary>
    public int Turn { get; }

    /// <summary>The operator's RNG stream for <paramref name="entityId"/> and <paramref name="purpose"/> this turn.</summary>
    public Rng Rng(int entityId, string purpose) => HistoryRng.Create(_seed, OperatorName, Turn, entityId, purpose);

    /// <summary>
    /// The key every draw about a person this operator proposes derives from: a pure function of the operator, the turn, the
    /// entity the person comes from (a polity, a settlement, a band) and a slot that tells this operator's persons of that
    /// entity apart. Unlike an id it does not depend on which other operators or plugins add persons this turn.
    /// </summary>
    public int StableKey(int originEntity, int slot) => HistoryRng.StableHash($"{OperatorName}|{Turn}|{originEntity}|{slot}");

    /// <summary>
    /// Emits <paramref name="person"/> and returns its pending handle (-2, -3, ... in emission order). This operator's other
    /// proposals of the turn may name the person by the handle wherever they take a person id; the commit resolves it, and
    /// drops them when the person is refused.
    /// </summary>
    public int ProposePerson(Proposal.AddPerson person)
    {
        ArgumentNullException.ThrowIfNull(person);
        person.Pending = -(2 + _pendingPersons++);
        Emit(person);
        return person.Pending;
    }

    /// <summary>
    /// An event participant with role <paramref name="role"/>, interned in the catalog's string table. Prefix the role with
    /// <see cref="ParticipantRoles.PersonPrefix"/> or <see cref="ParticipantRoles.SettlementPrefix"/> when
    /// <paramref name="entityId"/> is a person (or this operator's pending handle) or a settlement; a bare role means a polity.
    /// Interning is idempotent and runs in canonical operator order, so ids stay deterministic.
    /// </summary>
    public EventParticipant Participant(int entityId, string role)
    {
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("Role cannot be empty.", nameof(role));
        return new EventParticipant(entityId, _strings.Intern(role));
    }

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

    /// <summary>
    /// Emits an event of a plugin-defined kind. <paramref name="pluginKind"/> must be <c>namespace/name</c> in the
    /// emitting operator's own namespace.
    /// </summary>
    public void PluginEvent(string pluginKind, int polityId, int tile, IReadOnlyList<string> tags, int cause = -1,
        IReadOnlyList<EventParticipant>? participants = null)
    {
        RequireOwnName(pluginKind, nameof(pluginKind));
        Emit(new Proposal.AppendEvent
        {
            EntityId = polityId, EventKind = HistoryEventKind.Plugin, PluginKind = pluginKind,
            Tile = tile, Tags = tags, Cause = cause, Participants = participants,
        });
    }

    /// <summary>
    /// Stores a blob in the plugin state bag at commit. <paramref name="key"/> must be <c>namespace/name</c> in the
    /// emitting operator's own namespace; when several operators write one key in a turn the canonical order decides.
    /// </summary>
    public void SetState(string key, int version, ReadOnlySpan<byte> data)
    {
        RequireOwnName(key, nameof(key));
        Emit(new Proposal.SetPluginState { Key = key, Version = version, Data = data.ToArray() });
    }

    private void RequireOwnName(string name, string paramName)
    {
        HistoryNaming.RequirePluginName(name, paramName);
        if (!HistoryNaming.IsPluginName(OperatorName)
            || !string.Equals(HistoryNaming.NamespaceOf(name), HistoryNaming.NamespaceOf(OperatorName), StringComparison.Ordinal))
            throw new ArgumentException($"'{name}' is outside operator '{OperatorName}' namespace.", paramName);
    }

    /// <summary>Emits an event append that commits only when <paramref name="requires"/> (emitted this turn) is applied.</summary>
    public void EventIfApplied(Proposal requires, HistoryEventKind kind, int polityId, int tile, IReadOnlyList<string> tags, int cause = -1,
        IReadOnlyList<EventParticipant>? participants = null)
    {
        ArgumentNullException.ThrowIfNull(requires);
        Emit(new Proposal.AppendEvent { EntityId = polityId, EventKind = kind, Tile = tile, Tags = tags, Cause = cause, Participants = participants, Requires = requires });
    }

    /// <summary>Emits an event append.</summary>
    public void Event(HistoryEventKind kind, int polityId, int tile, IReadOnlyList<string> tags, int cause = -1,
        IReadOnlyList<EventParticipant>? participants = null)
        => Emit(new Proposal.AppendEvent { EntityId = polityId, EventKind = kind, Tile = tile, Tags = tags, Cause = cause, Participants = participants });
}
