using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// The X6 data (<c>Peoples/Data/Commerce/</c>, <c>Outlaws/</c>, <c>Intrigue/</c>, each read as <c>default</c>) compiled once per
/// operator, with the battle and court profiles it leans on and the helpers the commerce, merchant, outlaw and intrigue operators
/// share: the turn's routes, a polity's spymaster, the court's lean (<see cref="Hawk"/>). Names the history cannot place (a haven
/// terrain, a merchant tier) are listed in <see cref="UnknownKeys"/>.
/// </summary>
internal sealed class TradeProfile
{
    /// <summary>One route of the turn: its key, segment tiles, keepers (the holders of its capitals) and stored state.</summary>
    internal sealed class Route
    {
        public (int A, int B) Key;
        public readonly List<int> Tiles = new();
        public bool Severed;
        public int Keeper = -1, KeeperA = -1, KeeperB = -1;
        public double Safety = HistoryRouteState.NewRouteSafety, Traffic;
        public int Forts, Band = -1, Sponsor = -1, Sabotaged = -1;
    }

    private TradeProfile(CommerceDef commerce, OutlawDef outlaws, IntrigueDef intrigue, WarfareProfile warfare, CourtProfile court)
    {
        Commerce = commerce;
        Outlaws = outlaws;
        Intrigue = intrigue;
        Warfare = warfare;
        Court = court;
    }

    /// <summary>Trade tuning.</summary>
    public CommerceDef Commerce { get; }

    /// <summary>Outlaw tuning.</summary>
    public OutlawDef Outlaws { get; }

    /// <summary>Intrigue tuning.</summary>
    public IntrigueDef Intrigue { get; }

    /// <summary>Battle, fort and logistics tuning (terrain classes, who sails).</summary>
    public WarfareProfile Warfare { get; }

    /// <summary>Court tuning (offices, person rolls, death hazard).</summary>
    public CourtProfile Court { get; }

    /// <summary>True per terrain index for the ground outlaws hide in.</summary>
    public bool[] Haven { get; private set; } = Array.Empty<bool>();

    /// <summary>Smallest tier that holds merchants.</summary>
    public SettlementTier MerchantTier { get; private set; } = SettlementTier.Town;

    /// <summary>Plot names and weights in data order.</summary>
    public IReadOnlyList<(string Plot, double Weight)> Plots { get; private set; } = Array.Empty<(string, double)>();

    /// <summary>Agent operation names and weights in data order.</summary>
    public IReadOnlyList<(string Op, double Weight)> AgentOps { get; private set; } = Array.Empty<(string, double)>();

    /// <summary>Data names the history could not place.</summary>
    public IReadOnlyList<string> UnknownKeys { get; private set; } = Array.Empty<string>();

    /// <summary>Builds the profile from <paramref name="store"/>.</summary>
    public static TradeProfile From(PeoplesStore? store)
    {
        CommerceDef? commerce = null;
        OutlawDef? outlaws = null;
        IntrigueDef? intrigue = null;
        store?.Commerce.TryGetValue("default", out commerce);
        store?.Outlaws.TryGetValue("default", out outlaws);
        store?.Intrigues.TryGetValue("default", out intrigue);
        var p = new TradeProfile(commerce ?? new CommerceDef(), outlaws ?? new OutlawDef(), intrigue ?? new IntrigueDef(),
            WarfareProfile.From(store), CourtProfile.From(store));
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        var haven = new bool[p.Warfare.Terrains.Length];
        foreach (var t in p.Outlaws.HavenTerrains)
        {
            int i = p.Warfare.TerrainIndex(t);
            if (i < 0) unknown.Add($"terrain:{t}");
            else haven[i] = true;
        }
        p.Haven = haven;
        if (Enum.TryParse(p.Commerce.MerchantTier, ignoreCase: true, out SettlementTier tier) && Enum.IsDefined(typeof(SettlementTier), tier)) p.MerchantTier = tier;
        else unknown.Add($"tier:{p.Commerce.MerchantTier}");
        p.Plots = p.Intrigue.Plots.Select(kv => (kv.Key.ToLowerInvariant(), kv.Value)).ToList();
        p.AgentOps = p.Intrigue.AgentOps.Select(kv => (kv.Key.ToLowerInvariant(), kv.Value)).ToList();
        p.UnknownKeys = unknown.ToList();
        return p;
    }

    /// <summary>The turn's routes in key order (cached on the view).</summary>
    public static List<Route> Routes(TurnView view) => view.TradeRoutes;

    internal static List<Route> BuildRoutes(TurnView view)
    {
        var byKey = new SortedDictionary<(int, int), Route>();
        foreach (var r in view.Roads)
        {
            var key = (r.RouteA, r.RouteB);
            if (!byKey.TryGetValue(key, out var route))
                byKey[key] = route = new Route { Key = key, Keeper = r.PolityId, KeeperA = view.OwnerOf(r.RouteA), KeeperB = view.OwnerOf(r.RouteB) };
            route.Tiles.Add(r.TileA);
            route.Tiles.Add(r.TileB);
            route.Severed |= r.Severed;
        }
        foreach (var st in view.RouteStates)
            if (byKey.TryGetValue((st.RouteA, st.RouteB), out var route))
            {
                route.Safety = st.Safety;
                route.Traffic = st.Traffic;
                route.Forts = st.Forts;
                route.Band = st.OutlawBand;
                route.Sponsor = st.Sponsor;
                route.Sabotaged = st.SabotagedTurn;
            }
        return byKey.Values.ToList();
    }

    /// <summary>Angle in degrees between two tile vectors.</summary>
    public static double Degrees(Vector3D a, Vector3D b) => LogisticsGraph.Degrees(a, b);

    /// <summary>Smallest angle from <paramref name="tile"/> to a tile of <paramref name="route"/>.</summary>
    public static double DegreesTo(ReadOnlySpan<Vector3D> vectors, Route route, int tile)
    {
        double best = -1;
        foreach (int t in route.Tiles) best = Math.Max(best, Vector3D.Dot(vectors[t], vectors[tile]));
        return DetMath.Acos(Math.Max(-1, Math.Min(1, best))) * (180 / Math.PI);
    }

    /// <summary>True when a tile of <paramref name="route"/> lies within <paramref name="degrees"/> of <paramref name="tile"/> (one cosine, no arc cosines).</summary>
    public static bool Within(ReadOnlySpan<Vector3D> vectors, Route route, int tile, double degrees)
    {
        double limit = DetMath.Cos(degrees * (Math.PI / 180));
        var at = vectors[tile];
        foreach (int t in route.Tiles)
            if (Vector3D.Dot(vectors[t], at) >= limit) return true;
        return false;
    }

    /// <summary>Who runs <paramref name="polity"/>'s intrigue: the holder of the first held office of <see cref="IntrigueDef.PlotOffices"/>, else -1.</summary>
    public int Plotter(TurnView view, int polity)
    {
        if ((uint)polity >= (uint)view.Polities.Count) return -1;
        foreach (var office in Intrigue.PlotOffices)
        {
            int holder = view.OfficeHolder(polity, office);
            if (holder >= 0 && view.Persons[holder].Alive) return holder;
        }
        return -1;
    }

    /// <summary>The Intrigue of <paramref name="polity"/>'s <see cref="Plotter"/>, else <see cref="IntrigueDef.NoSpymaster"/>.</summary>
    public double Spy(TurnView view, int polity)
    {
        int plotter = Plotter(view, polity);
        return plotter >= 0 ? view.Persons[plotter].Intrigue : Intrigue.NoSpymaster;
    }

    /// <summary>
    /// How hawkish <paramref name="polity"/>'s court leans, 0..1: the mean of <c>(100 − A + E)/200</c> over its ruler (weight
    /// <see cref="IntrigueDef.RulerWeight"/>) and office holders (weight 1); 0.5 with nobody at court.
    /// </summary>
    public double Hawk(TurnView view, int polity)
    {
        if ((uint)polity >= (uint)view.Polities.Count) return 0.5;
        double sum = 0, weight = 0;
        void Add(int person, double w)
        {
            if (person < 0 || !view.Persons[person].Alive) return;
            var t = view.Persons[person].Traits;
            sum += w * (100 - CourtProfile.Scale(t.Agreeableness) + CourtProfile.Scale(t.Extraversion)) / 200.0;
            weight += w;
        }
        Add(view.OfficeHolder(polity, Proposal.SetTenure.RulerOffice), Intrigue.RulerWeight);
        foreach (var office in Court.OfficesOf(view.Polities[polity].Form)) Add(view.OfficeHolder(polity, office.Name), 1);
        return weight > 0 ? Math.Max(0, Math.Min(1, sum / weight)) : 0.5;
    }

    /// <summary>Living persons of <paramref name="kind"/> serving <paramref name="polity"/>, in id order.</summary>
    public static List<TurnView.PersonState> PersonsOf(TurnView view, int polity, PersonKind kind)
    {
        var list = new List<TurnView.PersonState>();
        foreach (var p in view.PersonsOf(polity))
            if (p.Alive && p.Kind == kind) list.Add(p);
        return list;
    }

    /// <summary>An absolute birth year for someone <paramref name="min"/>..<paramref name="max"/> of a lifespan old now (at least of age), drawn from <c>(key, "age")</c>.</summary>
    public static int BornAged(TurnView view, ProposalSink sink, PeoplesStore? store, int key, string folkKey, double min, double max)
    {
        var folk = HistoryDemography.ResolveFolk(folkKey, store);
        var rng = sink.Rng(key, "age");
        double share = min + (max - min) * rng.NextDouble();
        int age = Math.Max(folk.MaturationYears, (int)Math.Round(share * folk.LifespanYears));
        return view.Turn * view.TurnYears - age;
    }

    /// <summary>True when <paramref name="tile"/> borders the sea.</summary>
    public static bool Coastal(TurnView view, int tile)
    {
        var store = view.Map.DataStore;
        if (!store.IsLayerRegistered<ElevationInfo>()) return false;
        var elev = store.GetSpan<ElevationInfo>();
        Span<int> neighbors = stackalloc int[6];
        int n = store.GetAdjacent(tile, neighbors);
        for (int k = 0; k < n; k++)
            if ((uint)neighbors[k] < (uint)elev.Length && elev[neighbors[k]].Height < 0) return true;
        return false;
    }

    /// <summary>The routes within <see cref="CommerceDef.MerchantRouteDegrees"/> of <paramref name="tile"/>.</summary>
    public IEnumerable<Route> RoutesNear(ReadOnlySpan<Vector3D> vectors, List<Route> routes, int tile)
    {
        var near = new List<Route>();
        foreach (var r in routes)
            if (Within(vectors, r, tile, Commerce.MerchantRouteDegrees)) near.Add(r);
        return near;
    }

    /// <summary>True when <paramref name="person"/> holds an office of <paramref name="polity"/> (the court's, not a trader's, merchant prince).</summary>
    public bool HoldsOffice(TurnView view, int polity, int person)
    {
        if ((uint)polity >= (uint)view.Polities.Count) return false;
        if (view.OfficeHolder(polity, Proposal.SetTenure.RulerOffice) == person) return true;
        foreach (var office in Court.OfficesOf(view.Polities[polity].Form))
            if (view.OfficeHolder(polity, office.Name) == person) return true;
        return false;
    }
}
