using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 28: the turn-major driver. Per turn it snapshots the catalog into a <see cref="TurnView"/>,
/// runs every registered <see cref="IHistoryOperator"/> against that one view, then commits all
/// proposals in canonical order, so the result does not depend on registration order. After the last
/// turn it runs the <see cref="IHistoryPostPass"/>es.
/// </summary>
public sealed class HistoryDriver : HistoryPluginBase
{
    private readonly List<IHistoryOperator> _operators = new();
    private readonly List<IHistoryPostPass> _postPasses = new();

    /// <summary>Diagnostic name, <c>HistoryDriver</c>.</summary>
    public override string Name => "HistoryDriver";
    /// <summary><see cref="StageOrder.HistoryEvents"/> (28).</summary>
    public override int Order => StageOrder.HistoryEvents;
    /// <summary>None: the driver reads and writes the catalog only.</summary>
    public override Type[] Reads => Array.Empty<Type>();

    /// <summary>Proposals dropped at commit because a structural proposal on the same resource already won the turn.</summary>
    public int RejectedProposals { get; private set; }

    /// <summary>Registers an operator; names must be unique.</summary>
    public HistoryDriver AddOperator(IHistoryOperator op)
    {
        ArgumentNullException.ThrowIfNull(op);
        foreach (var existing in _operators)
            if (existing.Name == op.Name) throw new InvalidOperationException($"Operator '{op.Name}' is already registered.");
        _operators.Add(op);
        return this;
    }

    /// <summary>Registers a post-pass; names must be unique.</summary>
    public HistoryDriver AddPostPass(IHistoryPostPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        foreach (var existing in _postPasses)
            if (existing.Name == pass.Name) throw new InvalidOperationException($"Post-pass '{pass.Name}' is already registered.");
        _postPasses.Add(pass);
        return this;
    }

    /// <summary>True once polities have formed (even when zero formed on an all-ocean world).</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history) => history.PolitiesFormed;

    /// <summary>Runs turns 1 through <c>TurnCount - 1</c>, then the post-passes.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        context.ClearOperatorBudgets();
        RejectedProposals = 0;
        for (int turn = 1; turn < context.TurnCount; turn++) RunTurn(map, history, context, turn);
        history.SimulatedTurns = context.TurnCount > 0 ? context.TurnCount - 1 : 0;
        foreach (var pass in _postPasses) pass.Run(map, history, context);
    }

    /// <summary>Runs one turn: snapshot, propose, commit.</summary>
    public void RunTurn(WorldMap map, HistoryCatalog history, HistoryContext context, int turn)
    {
        context.BeginTurn(turn);
        var view = new TurnView(map, history, context, turn);
        var proposals = new List<Proposal>();
        var ordered = new List<IHistoryOperator>(_operators);
        ordered.Sort(static (a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
        foreach (var op in ordered)
        {
            var own = new List<Proposal>();
            op.Propose(view, new ProposalSink(op.Name, turn, history.Seed, context, own));
            proposals.AddRange(own);
        }
        Commit(history, turn, proposals);
    }

    private void Commit(HistoryCatalog history, int turn, List<Proposal> proposals)
    {
        proposals.Sort(static (a, b) =>
        {
            int c = a.EntityId.CompareTo(b.EntityId);
            if (c != 0) return c;
            c = a.Kind.CompareTo(b.Kind);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Operator, b.Operator);
            return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
        });
        var claimed = new HashSet<(ProposalKind, long)>();
        var deltas = new SortedDictionary<(int Polity, int Scalar), double>();
        foreach (var p in proposals)
        {
            if (p.ConflictKey is long key && !claimed.Add((p.Kind, key))) { RejectedProposals++; continue; }
            switch (p)
            {
                case Proposal.ReassignTile r:
                    if ((uint)r.Tile < (uint)history.Ownership.Length) history.Ownership[r.Tile] = r.EntityId;
                    break;
                case Proposal.AddRoad r:
                    history.Roads.Add(new HistoryRoad { TileA = r.TileA, TileB = r.TileB, PolityId = r.EntityId, RouteA = r.RouteA, RouteB = r.RouteB, FoundedTurn = turn });
                    break;
                case Proposal.SeverRoute s:
                    foreach (var road in history.Roads)
                        if (!road.Severed && road.RouteA == s.RouteA && road.RouteB == s.RouteB) { road.Severed = true; road.SeveredTurn = turn; }
                    break;
                case Proposal.AddHolySite h:
                    history.HolySites.Add(new HistoryHolySite { TileIndex = h.Tile, PolityId = h.EntityId, FoundedTurn = turn, Position = h.Position });
                    break;
                case Proposal.AddGrudge g:
                    history.AddGrudge(g.EntityId, g.Other, g.Weight, turn);
                    break;
                case Proposal.RepayGrudge g:
                    history.RepayGrudges(g.EntityId, g.Other);
                    break;
                case Proposal.AddWindow w:
                    history.AddWindow(w.EntityId, w.Other, w.UntilTurn);
                    break;
                case Proposal.AddSite st:
                    if (!history.Sites.Any(x => x.TileIndex == st.Tile))
                        history.Sites.Add(new HistorySite { TileIndex = st.Tile, PolityId = st.EntityId, FoundedTurn = turn, Position = st.Position, Active = true });
                    break;
                case Proposal.AppendEvent e:
                    history.AppendLinked(e.EventKind, turn, e.EntityId, e.Tile, e.Tags, e.Cause, e.Participants);
                    break;
                case Proposal.SetPopulation sp:
                    var target = history.Polities.Find(p => p.Id == sp.EntityId);
                    if (target != null) { target.Population = sp.Population; target.RulerGeneration = sp.RulerGeneration; }
                    break;
                case Proposal.AdjustScalar a:
                    var k = (a.EntityId, a.ScalarId);
                    deltas[k] = deltas.GetValueOrDefault(k) + a.Delta;
                    break;
            }
        }
        foreach (var ((polityId, scalarId), delta) in deltas)
        {
            var polity = history.Polities.Find(p => p.Id == polityId);
            if (polity == null) continue;
            polity.SetScalar(scalarId, polity.GetScalar(scalarId, history.Scalars) + delta, history.Scalars);
        }
    }
}
