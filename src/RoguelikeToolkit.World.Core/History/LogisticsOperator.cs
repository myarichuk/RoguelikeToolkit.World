using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 40, with <see cref="HistoryFeatures.Logistics"/> only: the projection of power along the roads. All tuning is data
/// (<c>Peoples/Data/Logistics/default.yaml</c>; the drift rate, fort guarding and severance are <c>Fortification/default.yaml</c>).
/// <list type="bullet">
/// <item>Every route's safety drifts toward <c>BaseSafety + ProjectionSafety × hold + FortSafety × forts + WaystationSafety ×
/// waystations − SeveredSafety[severed] − BelligerentSafety[at war] − OutlawSafety[outlaw band]</c>, clamped to 0..1. The hold
/// is the mean over the route's two keepers (the holders of its capitals) of <c>FormProjection × max(0, 1 − cost /
/// ProjectionCost)</c>, the cost being the keeper's network cost (<see cref="LogisticsGraph"/>) to the route's middle. A route is
/// at war while its keepers, or a keeper and a polity whose land the route crosses, fought within <c>WarTurns</c>. With
/// logistics on this operator owns the drift; <see cref="FortificationOperator"/> keeps only building forts.</item>
/// <item>A keeper builds a waystation (<see cref="SettlementRole.Waystation"/>, <c>why:waystation</c>) on a long, unsafe route
/// with too few of them, at the owned road tile farthest from any settlement. A waystation feeds hosts as a depot, cheapens its
/// road and adds a little safety.</item>
/// </list>
/// </summary>
public sealed class LogisticsOperator : IHistoryOperator
{
    /// <summary>The <c>why</c> of a waystation's founding.</summary>
    public const string WaystationWhy = "waystation";

    private PeoplesStore? _store;
    private bool _loaded;
    private WarfareProfile? _profile;

    /// <summary>Diagnostic name, <c>Logistics</c>.</summary>
    public string Name => "Logistics";

    /// <summary><see cref="StageOrder.Logistics"/> (40).</summary>
    public int Order => StageOrder.Logistics;

    internal WarfareProfile Profile => _profile ??= WarfareProfile.From(_store);

    private sealed class Route
    {
        public (int A, int B) Key;
        public readonly List<int> Tiles = new();
        public bool Severed;
        public int Keeper = -1;
        public double Length;
        public int Forts, Waystations;
        public double Safety = HistoryRouteState.NewRouteSafety;
        public int StateForts, StateWaystations, OutlawBand = -1;
    }

    /// <summary>Proposes the turn's waystations and road-safety drift.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (!view.Has(HistoryFeatures.Logistics) || view.Polities.Count == 0) return;
        if (!_loaded)
        {
            _store = view.LoadPeoples();
            _profile = null;
            _loaded = true;
        }
        var profile = Profile;
        var network = view.Logistics(profile);
        var routes = Routes(view, profile);
        foreach (var polity in view.Polities)
            if (polity.Alive) Waystation(view, sink, profile, polity, routes);
        Drift(view, sink, profile, network, routes);
    }

    private static double Degrees(Vector3D a, Vector3D b) => LogisticsGraph.Degrees(a, b);

    /// <summary>The routes of the turn's roads in key order, with their length, guards and stored state.</summary>
    private static List<Route> Routes(TurnView view, WarfareProfile profile)
    {
        var data = profile.Logistics;
        var vectors = view.Map.DataStore.GetTileVectors();
        var byKey = new SortedDictionary<(int, int), Route>();
        foreach (var r in view.Roads)
        {
            var key = (r.RouteA, r.RouteB);
            if (!byKey.TryGetValue(key, out var route)) byKey[key] = route = new Route { Key = key, Keeper = r.PolityId };
            route.Tiles.Add(r.TileA);
            route.Tiles.Add(r.TileB);
            route.Length += Degrees(vectors[r.TileA], vectors[r.TileB]);
            route.Severed |= r.Severed;
        }
        foreach (var st in view.RouteStates)
            if (byKey.TryGetValue((st.RouteA, st.RouteB), out var route))
            {
                route.Safety = st.Safety;
                route.StateForts = st.Forts;
                route.StateWaystations = st.Waystations;
                route.OutlawBand = st.OutlawBand;
            }
        var forts = profile.Forts;
        foreach (var route in byKey.Values)
            foreach (var s in view.Settlements)
            {
                if (s.Ended || (s.Role != SettlementRole.Post && s.Role != SettlementRole.Waystation)) continue;
                double reach = s.Role == SettlementRole.Post ? forts.GuardDegrees : data.WaystationDegrees;
                foreach (int t in route.Tiles)
                    if (view.OwnerOf(t) == s.PolityId && Degrees(vectors[t], vectors[s.Tile]) <= reach)
                    {
                        if (s.Role == SettlementRole.Post) route.Forts++;
                        else route.Waystations++;
                        break;
                    }
            }
        return byKey.Values.ToList();
    }

    private static bool Wants(LogisticsDef data, Route route)
        => route.Length >= data.WaystationLength && route.Safety < data.WaystationBelow
           && route.Waystations < (int)Math.Floor(route.Length / data.WaystationSpacing);

    private static void Waystation(TurnView view, ProposalSink sink, WarfareProfile profile, TurnView.PolityState polity, List<Route> routes)
    {
        var data = profile.Logistics;
        int standing = 0, stations = 0, towns = 0;
        foreach (var s in view.Settlements)
        {
            if (s.Ended || s.PolityId != polity.Id) continue;
            standing++;
            if (s.Role == SettlementRole.Waystation) stations++;
            else if (s.Role != SettlementRole.Post) towns++;
        }
        if (stations >= data.MaxWaystations || towns < Math.Max(1, profile.Forts.MinSettlements) || standing >= view.MaxSettlementsPerPolity) return;

        var candidates = new SortedSet<int>();
        foreach (var route in routes)
        {
            if (!Wants(data, route) || (view.OwnerOf(route.Key.A) != polity.Id && view.OwnerOf(route.Key.B) != polity.Id)) continue;
            foreach (int t in route.Tiles)
                if (view.OwnerOf(t) == polity.Id) candidates.Add(t);
        }
        if (candidates.Count == 0) return;
        double chance = data.WaystationChance * Steward(view, polity.Id, data);
        var rng = sink.Rng(polity.Id, "waystation");
        if (rng.NextDouble() >= Math.Min(1, chance)) return;

        var store = view.Map.DataStore;
        var vectors = store.GetTileVectors();
        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        int tile = -1;
        double best = data.WaystationClearance;
        foreach (int t in candidates)
        {
            if ((uint)t >= (uint)vectors.Length || (hasElev && elev[t].Height < 0)) continue;
            double gap = double.PositiveInfinity;
            foreach (var s in view.Settlements)
                if (!s.Ended) gap = Math.Min(gap, Degrees(vectors[t], vectors[s.Tile]));
            if (gap > best) { best = gap; tile = t; } // strictly farther: ties keep the smaller tile
        }
        if (tile < 0) return;
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
            EntityId = polity.Id, Tile = tile, Role = SettlementRole.Waystation, Tier = SettlementTier.Hamlet, Parent = parent, Why = WaystationWhy,
        });
    }

    /// <summary>The chance factor of the first held office of <see cref="LogisticsDef.WaystationOffices"/>: 0.5 + Stewardship/100, else 1.</summary>
    private static double Steward(TurnView view, int polity, LogisticsDef data)
    {
        foreach (var office in data.WaystationOffices)
        {
            int holder = view.OfficeHolder(polity, office);
            if (holder < 0) continue;
            foreach (var p in view.PersonsOf(polity))
                if (p.Id == holder) return 0.5 + p.Stewardship / 100.0;
        }
        return 1;
    }

    /// <summary>Drifts every route's safety toward its level and records its guarding forts and waystations.</summary>
    private static void Drift(TurnView view, ProposalSink sink, WarfareProfile profile, LogisticsGraph network, List<Route> routes)
    {
        var data = profile.Logistics;
        var forts = profile.Forts;
        foreach (var route in routes)
        {
            int keeperA = view.OwnerOf(route.Key.A), keeperB = view.OwnerOf(route.Key.B);
            int middle = route.Tiles[route.Tiles.Count / 2];
            double hold = 0.5 * (Hold(view, profile, network, keeperA, middle) + Hold(view, profile, network, keeperB, middle));
            bool war = keeperA >= 0 && keeperB >= 0 && keeperA != keeperB && network.AtWar(keeperA, keeperB);
            if (!war)
                foreach (int t in route.Tiles)
                {
                    int o = view.OwnerOf(t);
                    if (o >= 0 && ((keeperA >= 0 && o != keeperA && network.AtWar(o, keeperA)) || (keeperB >= 0 && o != keeperB && network.AtWar(o, keeperB))))
                    {
                        war = true;
                        break;
                    }
                }
            int guards = Math.Min(route.Forts, Math.Max(0, forts.SafetyForts));
            int stations = Math.Min(route.Waystations, Math.Max(0, data.WaystationsCounted));
            double target = data.BaseSafety + data.ProjectionSafety * hold + forts.FortSafety * guards + data.WaystationSafety * stations
                - (route.Severed ? forts.SeveredSafety : 0) - (war ? data.BelligerentSafety : 0) - (route.OutlawBand >= 0 ? data.OutlawSafety : 0);
            target = Math.Max(0, Math.Min(1, target));
            double drift = forts.SafetyDrift * (target - route.Safety);
            if (Math.Abs(drift) <= 1e-9 && guards == route.StateForts && route.Waystations == route.StateWaystations) continue;
            sink.Emit(new Proposal.AdjustRoute
            {
                EntityId = Math.Max(-1, route.Keeper), RouteA = route.Key.A, RouteB = route.Key.B, Safety = drift,
                Forts = guards - route.StateForts, Waystations = route.Waystations - route.StateWaystations,
            });
        }
    }

    /// <summary><paramref name="polity"/>'s hold on <paramref name="tile"/>, 0..1.</summary>
    private static double Hold(TurnView view, WarfareProfile profile, LogisticsGraph network, int polity, int tile)
    {
        if ((uint)polity >= (uint)view.Polities.Count || !view.Polities[polity].Alive) return 0;
        double cost = network.CostTo(polity, tile);
        if (double.IsPositiveInfinity(cost)) return 0;
        return profile.FormProjection(view.Polities[polity].Form) * Math.Max(0, 1 - cost / profile.Logistics.ProjectionCost);
    }
}
