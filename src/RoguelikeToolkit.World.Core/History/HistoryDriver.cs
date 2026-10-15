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

    /// <summary>Removes every registered operator and post-pass (for hosts and tests that supply their own ruleset).</summary>
    public HistoryDriver Clear() { _operators.Clear(); _postPasses.Clear(); return this; }

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
        Commit(map, history, context, turn, proposals);
    }

    private void Commit(WorldMap map, HistoryCatalog history, HistoryContext context, int turn, List<Proposal> proposals)
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
        if (AddPolities(map, history, context, turn, proposals)) proposals.RemoveAll(static p => p is Proposal.AddPolity);
        var claimed = new HashSet<(ProposalKind, long)>();
        var deltas = new SortedDictionary<(int Polity, int Scalar), double>();
        bool reassigned = false;
        var reassignedTiles = new HashSet<int>();
        var dirty = new HashSet<int>();
        foreach (var p in proposals)
        {
            if (p.ConflictKey is long key && !claimed.Add((p.Kind, key))) { RejectedProposals++; continue; }
            switch (p)
            {
                case Proposal.ReassignTile r:
                    if ((uint)r.Tile < (uint)history.Ownership.Length) { history.Ownership[r.Tile] = r.EntityId; reassigned = true; reassignedTiles.Add(r.Tile); }
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
                case Proposal.AddScar sc:
                    if (!string.IsNullOrWhiteSpace(sc.ScarKind) && !history.Scars.Any(x => x.TileIndex == sc.Tile && x.Kind == sc.ScarKind))
                        history.Scars.Add(new HistoryScar { TileIndex = sc.Tile, PolityId = sc.EntityId, FoundedTurn = turn, Kind = sc.ScarKind, Cause = sc.Cause ?? string.Empty, Position = sc.Position });
                    break;
                case Proposal.SetOverlay so:
                    var overlaid = history.Polities.Find(p => p.Id == so.EntityId);
                    if (overlaid != null) overlaid.CultureOverlay = so.Culture;
                    break;
                case Proposal.SetHeir sh:
                    var heirOwner = history.Polities.Find(p => p.Id == sh.EntityId);
                    if (heirOwner != null) heirOwner.HeirBornTurn = sh.HeirBornTurn;
                    break;
                case Proposal.AddVillain av:
                    var ruled = history.Polities.Find(p => p.Id == av.EntityId);
                    if (ruled == null || ruled.HasVillain || history.Villains.Count >= context.MaxVillains) { RejectedProposals++; break; }
                    history.Villains.Add(new VillainRecord
                    {
                        Id = history.Villains.Count, PolityId = ruled.Id, FolkKey = ruled.FolkKey, RisenTurn = turn, Agenda = av.Agenda,
                    });
                    ruled.HasVillain = true;
                    ruled.RulerGeneration = av.RulerGeneration;
                    break;
                case Proposal.DriftOpenness dr:
                    var drifting = history.Polities.Find(p => p.Id == dr.EntityId);
                    if (drifting != null && drifting.OpennessDrift < HistoryDemography.MaxOpennessDrift) drifting.OpennessDrift++;
                    break;
                case Proposal.AddSettlement ads:
                    if (!TryFoundSettlement(map, history, context, turn, ads, dirty)) RejectedProposals++;
                    break;
                case Proposal.EndSettlement ens:
                    if (!TryEndSettlement(history, turn, ens, dirty)) RejectedProposals++;
                    break;
                case Proposal.AppendEvent e:
                    history.AppendLinked(e.EventKind, turn, e.EntityId, e.Tile, e.Tags, e.Cause, e.Participants);
                    break;
                case Proposal.SetPopulation sp:
                    var target = history.Polities.Find(p => p.Id == sp.EntityId);
                    if (target != null) { target.Population = sp.Population; target.RulerGeneration = sp.RulerGeneration; }
                    break;
                case Proposal.GrowSettlement gs:
                    if ((uint)gs.Settlement < (uint)history.Settlements.Count)
                    {
                        var grown = history.Settlements[gs.Settlement];
                        if (!grown.Ended && grown.PolityId == gs.EntityId)
                        {
                            grown.Population = Math.Max(0, grown.Population + gs.Delta);
                            dirty.Add(grown.PolityId);
                        }
                    }
                    break;
                case Proposal.ReleasePopulation rp:
                    if (!TryRelease(history, turn, rp, dirty)) RejectedProposals++;
                    break;
                case Proposal.KillPopulation kp:
                    if (!TryKill(history, kp, dirty)) RejectedProposals++;
                    break;
                case Proposal.CaptureSettlement cap:
                    if (!TryCapture(history, context, cap, dirty)) RejectedProposals++;
                    break;
                case Proposal.AbsorbDisplaced ab:
                    if (!TryAbsorb(history, ab, dirty)) RejectedProposals++;
                    break;
                case Proposal.AdjustScalar a:
                    var k = (a.EntityId, a.ScalarId);
                    deltas[k] = deltas.GetValueOrDefault(k) + a.Delta;
                    break;
            }
        }
        if (reassigned) SyncSettlementOwners(history, reassignedTiles, dirty);
        DecayDisplaced(history, context, turn, dirty);
        ResyncPopulations(history, dirty, reassigned);
        foreach (var ((polityId, scalarId), delta) in deltas)
        {
            var polity = history.Polities.Find(p => p.Id == polityId);
            if (polity == null) continue;
            polity.SetScalar(scalarId, polity.GetScalar(scalarId, history.Scalars) + delta, history.Scalars);
        }
    }

    /// <summary>A settlement on a tile that just changed owner follows the new owner; settlements captured in war stay with their captor until their own tile is reassigned.</summary>
    private static void SyncSettlementOwners(HistoryCatalog history, HashSet<int> tiles, HashSet<int> dirty)
    {
        foreach (var settlement in history.Settlements)
        {
            if (settlement.Ended || !tiles.Contains(settlement.TileIndex)) continue;
            int owner = history.OwnerOf(settlement.TileIndex);
            if (owner < 0 || owner == settlement.PolityId) continue;
            dirty.Add(settlement.PolityId);
            dirty.Add(owner);
            settlement.PolityId = owner;
        }
    }

    /// <summary>A polity with standing settlements carries their summed population (its stock is the sum, never a second ledger).</summary>
    private static void ResyncPopulations(HistoryCatalog history, HashSet<int> dirty, bool all)
    {
        if (dirty.Count == 0) return;
        var sums = new Dictionary<int, double>();
        foreach (var settlement in history.Settlements)
        {
            if (settlement.Ended) continue;
            if (!all && !dirty.Contains(settlement.PolityId)) continue;
            sums[settlement.PolityId] = sums.GetValueOrDefault(settlement.PolityId) + settlement.Population;
        }
        foreach (var (polityId, sum) in sums)
        {
            var polity = history.Polities.Find(p => p.Id == polityId);
            if (polity != null) polity.Population = sum;
        }
        // A polity that lost its last standing settlement keeps no stock: its people are in the pool now.
        foreach (int id in dirty)
        {
            if (sums.ContainsKey(id)) continue;
            var polity = history.Polities.Find(p => p.Id == id);
            if (polity == null) continue;
            foreach (var settlement in history.Settlements)
                if (settlement.PolityId == id && settlement.Ended) { polity.Population = 0; break; }
        }
    }

    private static void Displace(HistoryCatalog history, int turn, int settlementIndex, double amount, int cause)
    {
        if (!(amount > 0)) return;
        var settlement = history.Settlements[settlementIndex];
        string culture = history.Polities.Find(p => p.Id == settlement.PolityId)?.CultureKey ?? string.Empty;
        history.Displaced.Add(new DisplacedGroup
        {
            PolityId = settlement.PolityId, Culture = culture, Turn = turn, Amount = amount, Origin = settlementIndex, Cause = cause,
        });
    }

    private static bool TryRelease(HistoryCatalog history, int turn, Proposal.ReleasePopulation rp, HashSet<int> dirty)
    {
        if ((uint)rp.Settlement >= (uint)history.Settlements.Count || !(rp.Share > 0) || rp.Share > 1) return false;
        var settlement = history.Settlements[rp.Settlement];
        if (settlement.Ended || settlement.PolityId != rp.EntityId) return false;
        double amount = settlement.Population * rp.Share;
        if (!(amount > 0)) return false;
        settlement.Population -= amount;
        Displace(history, turn, rp.Settlement, amount, rp.Cause);
        dirty.Add(settlement.PolityId);
        return true;
    }

    private static bool TryKill(HistoryCatalog history, Proposal.KillPopulation kp, HashSet<int> dirty)
    {
        if ((uint)kp.Settlement >= (uint)history.Settlements.Count || !(kp.Share > 0) || kp.Share > 1) return false;
        var settlement = history.Settlements[kp.Settlement];
        if (settlement.Ended || settlement.PolityId != kp.EntityId) return false;
        double dead = settlement.Population * kp.Share;
        if (!(dead > 0)) return false;
        settlement.Population -= dead;
        history.PopulationLost += dead;
        dirty.Add(settlement.PolityId);
        return true;
    }

    private static bool TryAbsorb(HistoryCatalog history, Proposal.AbsorbDisplaced ab, HashSet<int> dirty)
    {
        if ((uint)ab.Group >= (uint)history.Displaced.Count || (uint)ab.Settlement >= (uint)history.Settlements.Count || !(ab.Amount > 0)) return false;
        var group = history.Displaced[ab.Group];
        var target = history.Settlements[ab.Settlement];
        if (target.Ended || target.PolityId != ab.EntityId || group.PolityId != ab.EntityId) return false;
        double moved = Math.Min(group.Amount, ab.Amount);
        if (!(moved > 0)) return false;
        group.Amount -= moved;
        target.Population += moved;
        dirty.Add(target.PolityId);
        return true;
    }

    /// <summary>
    /// Ages the pool: a group nobody took in for <see cref="HistoryContext.DisplacedDecayTurns"/> turns is settled by decree
    /// into its origin polity's first standing settlement, or recorded as lost when that polity has none. Drained groups are dropped.
    /// </summary>
    private static void DecayDisplaced(HistoryCatalog history, HistoryContext context, int turn, HashSet<int> dirty)
    {
        if (history.Displaced.Count == 0) return;
        foreach (var group in history.Displaced)
        {
            if (!(group.Amount > 0) || turn - group.Turn < context.DisplacedDecayTurns) continue;
            HistorySettlement? home = null;
            foreach (var s in history.Settlements)
                if (!s.Ended && s.PolityId == group.PolityId) { home = s; break; }
            if (home != null) { home.Population += group.Amount; dirty.Add(home.PolityId); }
            else history.PopulationLost += group.Amount;
            group.Amount = 0;
        }
        history.Displaced.RemoveAll(static g => !(g.Amount > 0));
    }

    private static bool TryFoundSettlement(WorldMap map, HistoryCatalog history, HistoryContext context, int turn, Proposal.AddSettlement ads, HashSet<int> dirty)
    {
        var owner = history.Polities.Find(p => p.Id == ads.EntityId);
        if (owner == null || !owner.Alive) return false;
        if ((uint)ads.Tile >= (uint)history.Ownership.Length || history.Ownership[ads.Tile] != owner.Id) return false;
        if (ads.Parent < -1 || ads.Parent >= history.Settlements.Count) return false;
        int standing = 0;
        foreach (var s in history.Settlements)
        {
            if (s.Ended) continue;
            if (s.TileIndex == ads.Tile) return false;
            if (s.PolityId == owner.Id) standing++;
        }
        if (standing >= context.MaxSettlementsPerPolity) return false;
        int index = history.Settlements.Count;
        var store = map.DataStore;
        history.Settlements.Add(new HistorySettlement
        {
            TileIndex = ads.Tile, PolityId = owner.Id, FoundedTurn = turn, Position = store.GetGeoCoord(ads.Tile),
            Habitability = CapitalSettlements.HabitabilityOf(store, ads.Tile), Active = true,
            Tier = ads.Tier, Role = ads.Role, Parent = ads.Parent,
        });
        HistorySettlement? seeded = null;
        if ((uint)ads.FromGroup < (uint)history.Displaced.Count && history.Displaced[ads.FromGroup].PolityId == owner.Id
            && history.Displaced[ads.FromGroup].Amount > 0)
        {
            var group = history.Displaced[ads.FromGroup];
            history.Settlements[index].Population = group.Amount;
            group.Amount = 0;
            seeded = history.Settlements[index];
            dirty.Add(owner.Id);
        }
        if (seeded == null && ads.Parent >= 0)
        {
            var parent = history.Settlements[ads.Parent];
            if (!parent.Ended && parent.PolityId == owner.Id)
            {
                double seed = parent.Population * HistoryDemography.ColonistShare;
                parent.Population -= seed;
                history.Settlements[index].Population = seed;
            }
        }
        var tags = new List<string> { $"settlement:{index}", $"role:{ads.Role}" };
        if (ads.Parent >= 0) tags.Add($"from:{ads.Parent}");
        history.AppendLinked(HistoryEventKind.SettlementFounded, turn, owner.Id, ads.Tile, tags, ads.Cause, null);
        return true;
    }

    private static bool TryCapture(HistoryCatalog history, HistoryContext context, Proposal.CaptureSettlement cap, HashSet<int> dirty)
    {
        if ((uint)cap.Settlement >= (uint)history.Settlements.Count) return false;
        var settlement = history.Settlements[cap.Settlement];
        if (settlement.Ended || settlement.PolityId == cap.EntityId || settlement.Role == SettlementRole.Capital) return false;
        var captor = history.Polities.Find(p => p.Id == cap.EntityId);
        if (captor == null || !captor.Alive) return false;
        int standing = 0;
        foreach (var s in history.Settlements)
            if (!s.Ended && s.PolityId == captor.Id) standing++;
        if (standing >= context.MaxSettlementsPerPolity) return false;
        dirty.Add(settlement.PolityId);
        dirty.Add(captor.Id);
        settlement.PolityId = captor.Id;
        return true;
    }

    private static bool TryEndSettlement(HistoryCatalog history, int turn, Proposal.EndSettlement ens, HashSet<int> dirty)
    {
        if ((uint)ens.Settlement >= (uint)history.Settlements.Count) return false;
        var settlement = history.Settlements[ens.Settlement];
        if (settlement.Ended || settlement.PolityId != ens.EntityId) return false;
        settlement.EndedTurn = turn;
        settlement.EndCause = ens.EndCause;
        int ended = history.AppendLinked(HistoryEventKind.SettlementEnded, turn, settlement.PolityId, settlement.TileIndex,
            new[] { $"settlement:{ens.Settlement}", ens.EndCause }, ens.Cause, null);
        double people = settlement.Population;
        double dead = people * Math.Clamp(ens.LostShare, 0, 1);
        history.PopulationLost += dead;
        settlement.Population = 0;
        Displace(history, turn, ens.Settlement, people - dead, ended);
        dirty.Add(settlement.PolityId);
        return true;
    }

    /// <summary>
    /// Structural pre-pass: founds the turn's new polities in id order (ids must be contiguous and the
    /// polity cap holds), then re-floods ownership from every capital and syncs holy-site owners.
    /// Returns true when the turn had any <see cref="Proposal.AddPolity"/> (all of them are consumed).
    /// </summary>
    private bool AddPolities(WorldMap map, HistoryCatalog history, HistoryContext context, int turn, List<Proposal> proposals)
    {
        bool any = false;
        bool founded = false;
        foreach (var p in proposals)
        {
            if (p is not Proposal.AddPolity ap) continue;
            any = true;
            if (ap.EntityId != history.Polities.Count || history.Polities.Count >= context.MaxPolities) { RejectedProposals++; continue; }
            var polity = new Polity
            {
                Id = ap.EntityId, Form = ap.Form, CapitalTile = ap.CapitalTile, FolkKey = ap.FolkKey,
                CultureKey = ap.CultureKey, FoundedTurn = turn, Parent = ap.Parent,
            };
            history.Polities.Add(polity);
            CapitalSettlements.Add(map.DataStore, history, polity, turn);
            founded = true;
        }
        if (!founded) return any;
        var capitals = new List<int>(history.Polities.Count);
        foreach (var polity in history.Polities) capitals.Add(polity.CapitalTile);
        var pins = new Dictionary<int, int>();
        foreach (var kv in history.Overrides.PinnedTiles)
            if (kv.Value >= 0 && kv.Value < history.Polities.Count) pins[kv.Key] = kv.Value;
        history.Ownership = HistoryTerritory.AssignOwners(map.DataStore, capitals, pins);
        foreach (var site in history.HolySites)
        {
            int owner = history.OwnerOf(site.TileIndex);
            if (owner >= 0) site.PolityId = owner;
        }
        return any;
    }
}
