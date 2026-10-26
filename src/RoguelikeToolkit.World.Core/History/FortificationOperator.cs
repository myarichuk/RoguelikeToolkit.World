using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 42, with <see cref="HistoryFeatures.Sieges"/> only: forts, as defence and as projection of power. All tuning is data
/// (<c>Peoples/Data/Fortification/default.yaml</c>; a people's appetite is <c>Fortify</c> and its kind of fort <c>FortKind</c> in
/// <c>Warfare/&lt;culture&gt;.yaml</c>).
/// <list type="bullet">
/// <item>A polity builds a fort when it needs one: a bordering polity it holds a grudge against, an invasion within the last
/// few turns, or an own road no fort guards. The chance is <c>FortChance × Fortify/100 × need</c>; it holds at most
/// <c>ceil(MaxForts × Fortify/100)</c> military posts. The fort stands on its own land facing the threat (or on the unguarded
/// road), on high ground or a river where it can, apart from other settlements. A fort is a garrison post
/// (<see cref="SettlementRole.Post"/>, <c>why:fort</c>) manned from its nearest settlement.</item>
/// <item>Without <see cref="HistoryFeatures.Logistics"/> (with it <see cref="LogisticsOperator"/> owns the drift): every road's safety drifts toward a level that each guarding post raises and a severance lowers; the route state
/// records the guarding forts. Outlaws (X6) read that safety.</item>
/// </list>
/// In battle a post's garrison joins a defence nearby and its watch warns of an invasion (<see cref="WarOperator"/>); in a
/// siege a fort's own walls and garrison hold it.
/// </summary>
public sealed class FortificationOperator : IHistoryOperator
{
    /// <summary>The <c>why</c> of a fort's founding.</summary>
    public const string FortWhy = "fort";

    private PeoplesStore? _store;
    private bool _loaded;
    private WarfareProfile? _profile;

    /// <summary>Diagnostic name, <c>Fortification</c>.</summary>
    public string Name => "Fortification";

    /// <summary><see cref="StageOrder.Fortification"/> (42).</summary>
    public int Order => StageOrder.Fortification;

    internal WarfareProfile Profile => _profile ??= WarfareProfile.From(_store);

    /// <summary>Proposes the turn's forts and road-safety drift.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (!view.Has(HistoryFeatures.Sieges) || view.Polities.Count == 0) return;
        if (!_loaded)
        {
            _store = view.LoadPeoples();
            _profile = null;
            _loaded = true;
        }
        var profile = Profile;
        foreach (var polity in view.Polities)
            if (polity.Alive) Build(view, sink, profile, polity);
        // With logistics on, LogisticsOperator owns the road drift (and projection).
        if (!view.Has(HistoryFeatures.Logistics)) Roads(view, sink, profile);
    }

    private static double Degrees(Vector3D a, Vector3D b) => DetMath.Acos(Math.Max(-1, Math.Min(1, Vector3D.Dot(a, b)))) * (180 / Math.PI);

    private static void Build(TurnView view, ProposalSink sink, WarfareProfile profile, TurnView.PolityState polity)
    {
        var f = profile.Forts;
        var way = profile.WayOf(polity.CultureKey);
        double fortify = Math.Max(0, Math.Min(1, way.Fortify / 100.0));
        if (fortify <= 0 || way.Fort == null) return;
        int standing = 0, posts = 0, stations = 0;
        foreach (var s in view.Settlements)
        {
            if (s.Ended || s.PolityId != polity.Id) continue;
            standing++;
            if (s.Role == SettlementRole.Post) posts++;
            else if (s.Role == SettlementRole.Waystation) stations++;
        }
        int cap = Math.Max(1, (int)Math.Ceiling(f.MaxForts * fortify));
        if (standing - posts - stations < Math.Max(1, f.MinSettlements) || posts >= cap || standing >= view.MaxSettlementsPerPolity) return;

        var store = view.Map.DataStore;
        var vectors = store.GetTileVectors();
        var candidates = new SortedSet<int>();
        double need = 0;
        bool threatened = false;
        foreach (int rival in view.BorderingPolities(polity.Id))
        {
            if (view.GrudgeWeight(polity.Id, rival) <= 0 || view.IsWindowed(polity.Id, rival)) continue;
            threatened = true;
            foreach (int t in view.FrontierTiles(rival, polity.Id)) candidates.Add(t);
        }
        if (threatened) need += f.ThreatNeed;
        bool invaded = false;
        foreach (var b in view.Battles)
        {
            if (b.Defender != polity.Id || b.Turn < view.Turn - f.RecentTurns || b.Kind == BattleKind.Siege || b.Kind == BattleKind.Relief) continue;
            if (b.Outcome == BattleOutcome.Averted) continue;
            invaded = true;
            if ((uint)b.Attacker < (uint)view.Polities.Count)
                foreach (int t in view.FrontierTiles(b.Attacker, polity.Id)) candidates.Add(t);
        }
        if (invaded) need += f.InvadedNeed;
        var roadTiles = new SortedSet<int>();
        bool unguarded = false;
        foreach (var r in view.Roads)
        {
            foreach (int t in new[] { r.TileA, r.TileB })
            {
                if (view.OwnerOf(t) != polity.Id || !roadTiles.Add(t) || Guarded(view, vectors, polity.Id, t, f.GuardDegrees)) continue;
                unguarded = true;
                candidates.Add(t);
            }
        }
        if (unguarded) need += f.RouteNeed;
        if (need <= 0) return;
        var rng = sink.Rng(polity.Id, "fort");
        if (rng.NextDouble() >= Math.Min(1, f.FortChance * fortify * need)) return;

        bool hasElev = store.IsLayerRegistered<ElevationInfo>(), hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;
        var best = new List<int>();
        int bestScore = int.MinValue;
        foreach (int t in candidates)
        {
            if ((uint)t >= (uint)vectors.Length || view.OwnerOf(t) != polity.Id || (hasElev && elev[t].Height < 0)) continue;
            bool spaced = true;
            foreach (var s in view.Settlements)
                if (!s.Ended && (s.Tile == t || Degrees(vectors[t], vectors[s.Tile]) < f.SpacingDegrees)) { spaced = false; break; }
            if (!spaced) continue;
            int score = (hasElev && elev[t].Height >= f.HighFrom ? 2 : 0) + (hasHydro && hydro[t].IsRiver != 0 ? 1 : 0) + (roadTiles.Contains(t) ? 1 : 0);
            if (score > bestScore) { bestScore = score; best.Clear(); }
            if (score == bestScore) best.Add(t);
        }
        if (best.Count == 0) return;
        int tile = best[(int)rng.NextUInt((uint)best.Count)];
        int parent = -1;
        double nearest = double.NegativeInfinity;
        foreach (var s in view.Settlements)
        {
            if (s.Ended || s.PolityId != polity.Id) continue;
            double dot = Vector3D.Dot(vectors[tile], vectors[s.Tile]);
            if (dot > nearest) { nearest = dot; parent = s.Index; }
        }
        if (parent < 0 || !sink.TrySpendEmergence()) return;
        sink.Emit(new Proposal.AddSettlement
        {
            EntityId = polity.Id, Tile = tile, Role = SettlementRole.Post, Tier = SettlementTier.Hamlet, Parent = parent, Why = FortWhy,
        });
    }

    private static bool Guarded(TurnView view, ReadOnlySpan<Vector3D> vectors, int polity, int tile, double degrees)
    {
        foreach (var s in view.Settlements)
            if (!s.Ended && s.Role == SettlementRole.Post && s.PolityId == polity && Degrees(vectors[tile], vectors[s.Tile]) <= degrees) return true;
        return false;
    }

    /// <summary>Drifts every road's safety toward its level: base, plus the guarding posts, minus a severance.</summary>
    private static void Roads(TurnView view, ProposalSink sink, WarfareProfile profile)
    {
        var f = profile.Forts;
        var vectors = view.Map.DataStore.GetTileVectors();
        var routes = new SortedDictionary<(int A, int B), (List<int> Tiles, bool Severed, int Keeper)>();
        foreach (var r in view.Roads)
        {
            var key = (r.RouteA, r.RouteB);
            if (!routes.TryGetValue(key, out var route)) routes[key] = route = (new List<int>(), false, r.PolityId);
            route.Tiles.Add(r.TileA);
            route.Tiles.Add(r.TileB);
            if (r.Severed) routes[key] = (route.Tiles, true, route.Keeper);
        }
        var states = new Dictionary<(int, int), TurnView.RouteStateView>();
        foreach (var st in view.RouteStates) states[(st.RouteA, st.RouteB)] = st;
        foreach (var (key, route) in routes)
        {
            int guards = 0;
            foreach (var s in view.Settlements)
            {
                if (s.Ended || s.Role != SettlementRole.Post) continue;
                foreach (int t in route.Tiles)
                    if (view.OwnerOf(t) == s.PolityId && Degrees(vectors[t], vectors[s.Tile]) <= f.GuardDegrees) { guards++; break; }
            }
            guards = Math.Min(guards, Math.Max(0, f.SafetyForts));
            double safety = HistoryRouteState.NewRouteSafety;
            int forts = 0;
            if (states.TryGetValue(key, out var state)) { safety = state.Safety; forts = state.Forts; }
            double target = Math.Max(0, Math.Min(1, HistoryRouteState.NewRouteSafety + f.FortSafety * guards - (route.Severed ? f.SeveredSafety : 0)));
            double drift = f.SafetyDrift * (target - safety);
            if (Math.Abs(drift) <= 1e-9 && guards == forts) continue;
            sink.Emit(new Proposal.AdjustRoute { EntityId = Math.Max(-1, route.Keeper), RouteA = key.A, RouteB = key.B, Safety = drift, Forts = guards - forts });
        }
    }
}
