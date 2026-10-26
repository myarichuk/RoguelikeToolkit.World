using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

public sealed partial class WarOperator
{
    /// <summary>Tag of a campaign that ran out of supply before it reached the field.</summary>
    public const string SupplyFailedTag = "supply_failed";

    /// <summary>Tag of a campaign whose attacker refused a hopeless field and turned back.</summary>
    public const string DeclinedTag = "declined";

    /// <summary>Tag of a war or campaign in which merchants' pledges hired mercenaries (commerce on).</summary>
    public const string MercenariesTag = "mercenaries";

    /// <summary>Tag of a war or campaign whose host wintered in quarters and marched in the spring (logistics on).</summary>
    public const string WinterQuartersTag = "winter_quarters";

    private WarfareProfile? _warfare;
    private CourtProfile? _court;

    internal WarfareProfile Warfare => _warfare ??= WarfareProfile.From(_store);

    private CourtProfile Court => _court ??= CourtProfile.From(_store);

    /// <summary>The <c>outcome:</c> tag value of <paramref name="outcome"/> (<c>attacker_decisive</c>...).</summary>
    public static string OutcomeTag(BattleOutcome outcome)
    {
        string name = outcome.ToString();
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Fights the war: candidate fields from the frontier, both sides from the warfare data and the court, the resolver's
    /// verdict as a battle record, then its consequences. Every draw keys on <c>StableKey(attacker, defender)</c>.
    /// </summary>
    private void Fight(TurnView view, ProposalSink sink, HashSet<(int, int)> severedNow, HashSet<int> sitedNow,
        TurnView.PolityState attacker, TurnView.PolityState defender)
    {
        var profile = Warfare;
        var data = profile.Data;
        int key = sink.StableKey(attacker.Id, defender.Id);
        var a = SideOf(view, profile, attacker);
        var d = SideOf(view, profile, defender);
        bool sieges = view.Has(HistoryFeatures.Sieges);
        bool logistics = view.Has(HistoryFeatures.Logistics);
        var fields = Fields(view, sink.Rng(key, "battle:fields"), profile, attacker.Id, defender, a.Way, d.Way, sieges,
            logistics ? view.Logistics(profile) : null);
        var result = BattleResolver.Resolve(profile, a, d, fields, purpose => sink.Rng(key, purpose), view.TurnYears, winterQuarters: logistics);
        var field = fields[result.Field];
        int tile = field.Tile;
        var tags = new List<string>
        {
            result.Outcome == BattleOutcome.Averted ? "campaign" : "war", $"attacker:{attacker.Id}", $"defender:{defender.Id}",
            $"outcome:{OutcomeTag(result.Outcome)}",
        };
        if (result.Outcome == BattleOutcome.Averted) tags.Add(result.Declined ? DeclinedTag : SupplyFailedTag);
        if (result.Wintered) tags.Add(WinterQuartersTag);
        if (view.Has(HistoryFeatures.Commerce) && (Pledges(view, sink, attacker.Id) | Pledges(view, sink, defender.Id))) tags.Add(MercenariesTag);
        var participants = new List<EventParticipant>();
        if (a.General != null) participants.Add(sink.Participant(a.General.Person, ParticipantRoles.PersonPrefix + "attacker_general"));
        if (d.General != null) participants.Add(sink.Participant(d.General.Person, ParticipantRoles.PersonPrefix + "defender_general"));
        var battle = new Proposal.AddBattle
        {
            EntityId = attacker.Id,
            EventKind = result.Outcome == BattleOutcome.Averted ? HistoryEventKind.Campaign : HistoryEventKind.War,
            Tags = tags,
            Participants = participants.Count > 0 ? participants : null,
            Year = result.Year, Season = result.Season, BattleKind = result.Kind, Tile = tile,
            Terrain = profile.Terrains[result.Terrain],
            Attacker = SideRecord(profile, a, result.Attacker),
            Defender = SideRecord(profile, d, result.Defender),
            Outcome = result.Outcome,
            Beats = result.Beats,
        };
        sink.Emit(battle);

        SeverRoutes(view, sink, severedNow, attacker.Id, attacker.CapitalTile, defender.CapitalTile);
        sink.Adjust(attacker.Id, ScalarRegistry.Pressure, HistoryDemography.WarPressureBump);
        sink.Adjust(defender.Id, ScalarRegistry.Pressure, HistoryDemography.WarPressureBump);
        if (result.Outcome == BattleOutcome.Averted) return;

        if (!view.HasSite(tile) && sitedNow.Add(tile))
        {
            sink.Emit(new Proposal.AddScar
            {
                EntityId = attacker.Id, Tile = tile, ScarKind = HistoryScar.Battlefield, Cause = "war",
                Position = view.Map.DataStore.GetGeoCoord(tile),
            });
        }
        CheckDesecration(view, sink, attacker.Id, defender.Id, tile);
        Casualties(view, sink, profile, attacker.Id, tile, result.Attacker, result.DefenderWon ? d.Way : null);
        Casualties(view, sink, profile, defender.Id, tile, result.Defender, result.AttackerWon ? a.Way : null);
        Commanders(view, sink, data, a, d, result);
        Aftermath(view, sink, severedNow, profile, attacker, defender, a, d, tile, result, battle, key, sieges);
    }

    /// <summary>
    /// Up to <see cref="BattleDef.FieldCandidates"/> distinct tiles drawn from the attacker's frontier into the defender (the
    /// defender's capital when they share none), each described by its ground, river, forage, seasons, the attacker's distance
    /// from its nearest settlement, whose holy ground it is, and the share of each side's people near enough to muster there.
    /// With <paramref name="network"/> (logistics on) the attacker's distance is its network cost to the field instead.
    /// </summary>
    private static List<BattleResolver.Field> Fields(TurnView view, Rng rng, WarfareProfile profile, int attacker, TurnView.PolityState defender,
        WarfareProfile.Way attackerWay, WarfareProfile.Way defenderWay, bool sieges, LogisticsGraph? network = null)
    {
        var forts = profile.Forts;
        var fort = sieges ? defenderWay.Fort : null;
        double musterScaleA = profile.Data.MusterDegrees * (0.5 + attackerWay.Mobility / 100.0);
        double musterScaleD = profile.Data.MusterDegrees * (0.5 + defenderWay.Mobility / 100.0);
        var frontier = view.FrontierTiles(attacker, defender.Id);
        var tiles = new List<int>();
        if (frontier.Count == 0) tiles.Add(defender.CapitalTile);
        else
        {
            var pool = new List<int>(frontier);
            int take = Math.Min(Math.Max(1, profile.Data.FieldCandidates), pool.Count);
            for (int i = 0; i < take; i++)
            {
                int j = i + (int)rng.NextUInt((uint)(pool.Count - i));
                (pool[i], pool[j]) = (pool[j], pool[i]);
                tiles.Add(pool[i]);
            }
        }
        var store = view.Map.DataStore;
        var vectors = store.GetTileVectors();
        bool hasBiome = store.IsLayerRegistered<BiomeInfo>(), hasElev = store.IsLayerRegistered<ElevationInfo>();
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>(), hasClimate = store.IsLayerRegistered<ClimateInfo>();
        bool hasLand = store.IsLayerRegistered<LandCapacityInfo>();
        var biomes = hasBiome ? store.GetSpan<BiomeInfo>() : default;
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;
        var land = hasLand ? store.GetSpan<LandCapacityInfo>() : default;
        double forageWeight = 0;
        foreach (double w in profile.Forage) forageWeight += w;
        var fields = new List<BattleResolver.Field>(tiles.Count);
        foreach (int tile in tiles)
        {
            int terrain = hasBiome ? profile.TerrainOf(biomes[tile].Biome, hasElev ? elev[tile].Height : 0) : profile.DefaultTerrain;
            double forage = 0;
            if (hasLand && forageWeight > 0)
            {
                var l = land[tile];
                forage = (profile.Forage[0] * l.Arable + profile.Forage[1] * l.Pasture + profile.Forage[2] * l.Game
                    + profile.Forage[3] * l.Timber + profile.Forage[4] * l.Fish) / (255.0 * forageWeight);
            }
            // The attacker gathers at its settlement nearest the field and marches from there (the march is the resolver's
            // reach); the defender gathers where it stands.
            double distance = 180, musterA = 0, musterD = 0, popA = 0, popD = 0;
            int staging = -1;
            foreach (var s in view.Settlements)
            {
                if (s.Ended) continue;
                double d = Degrees(vectors[tile], vectors[s.Tile]);
                if (s.PolityId == attacker && d < distance) { distance = d; staging = s.Tile; }
                else if (s.PolityId == defender.Id)
                {
                    musterD += s.Population * DetMath.Exp(-d / musterScaleD);
                    popD += s.Population;
                }
            }
            if (staging >= 0)
                foreach (var s in view.Settlements)
                    if (!s.Ended && s.PolityId == attacker)
                    {
                        musterA += s.Population * DetMath.Exp(-Degrees(vectors[staging], vectors[s.Tile]) / musterScaleA);
                        popA += s.Population;
                    }
            int holy = -1;
            foreach (var site in view.HolySites)
                if (site.Active && site.Tile == tile) { holy = site.PolityId; break; }
            // With sieges on, the defender's posts stand guard: their garrisons join a defence nearby, their watch warns of it.
            double garrison = 0, watch = 0;
            if (fort != null)
                foreach (var s in view.Settlements)
                {
                    if (s.Ended || s.PolityId != defender.Id || s.Role != SettlementRole.Post) continue;
                    double d = Degrees(vectors[tile], vectors[s.Tile]);
                    garrison += fort.Garrison * DetMath.Exp(-d / forts.GarrisonDegrees);
                    if (d <= forts.WatchDegrees) watch = Math.Max(watch, fort.Watch);
                }
            double reachScale = 0;
            if (network != null)
            {
                double cost = network.CostTo(attacker, tile, defender.Id);
                distance = double.IsPositiveInfinity(cost) ? 1e6 : cost;
                reachScale = profile.Logistics.ReachCost;
            }
            fields.Add(new BattleResolver.Field(tile, terrain, hasHydro && hydro[tile].IsRiver != 0, forage,
                hasClimate ? climate[tile].SeasonalRange : 0, distance, holy, popA > 0 ? musterA / popA : 0, popD > 0 ? musterD / popD : 1,
                garrison, watch, reachScale));
        }
        return fields;
    }

    private static double Degrees(Vector3D a, Vector3D b) => DetMath.Acos(Math.Max(-1, Math.Min(1, Vector3D.Dot(a, b)))) * (180 / Math.PI);

    /// <summary>A polity as a battle side: its levy, its culture's way of war and honor, its commander and its spymaster.</summary>
    private BattleResolver.Side SideOf(TurnView view, WarfareProfile profile, TurnView.PolityState polity)
    {
        var way = profile.WayOf(polity.CultureKey);
        CultureDef? culture = null;
        _store?.Cultures.TryGetValue(polity.CultureKey, out culture);
        double collectivism = culture?.Collectivism ?? 50;
        double levy = polity.Population * profile.FormLevy(polity.Form) * way.Levy * Math.Max(0, 1 + profile.Data.CollectivismLevy * (collectivism - 50));
        BattleResolver.Commander? general = null;
        double spy = 50;
        foreach (var office in Court.OfficesOf(polity.Form))
        {
            int holder = view.OfficeHolder(polity.Id, office.Name);
            if (holder < 0 || !view.Persons[holder].Alive) continue;
            if (office.Kind == PersonKind.General && general == null) general = CommanderOf(view.Persons[holder]);
            if (office.Kind == PersonKind.Spymaster && spy == 50) spy = view.Persons[holder].Intrigue;
        }
        if (general == null)
        {
            int ruler = view.OfficeHolder(polity.Id, Proposal.SetTenure.RulerOffice);
            if (ruler >= 0 && view.Persons[ruler].Alive) general = CommanderOf(view.Persons[ruler]);
        }
        // With commerce on, the merchants' pledges hire mercenaries (spent by the war: Pledges).
        if (view.Has(HistoryFeatures.Commerce))
        {
            double pledged = 0;
            foreach (var p in view.PersonsOf(polity.Id)) pledged += Math.Max(0, p.Pledged);
            var c = Trade.Commerce;
            levy += Math.Min(pledged * c.MercenaryPerWealth, c.MercenaryCap * levy);
        }
        return new BattleResolver.Side(polity.Id, levy, way, general, spy, culture?.Honor ?? 50, view.ScalarOf(polity.Id, ScalarRegistry.Pressure));
    }

    /// <summary>Spends every pledge of <paramref name="polity"/>'s people (the war hired mercenaries with them); true when there were any.</summary>
    private static bool Pledges(TurnView view, ProposalSink sink, int polity)
    {
        bool any = false;
        foreach (var p in view.PersonsOf(polity))
        {
            if (p.Pledged <= 0) continue;
            sink.Emit(new Proposal.AdjustPerson { EntityId = polity, Person = p.Id, Pledged = -p.Pledged });
            any = true;
        }
        return any;
    }

    private static BattleResolver.Commander CommanderOf(TurnView.PersonState p) => new(p.Id, p.Martial, p.Intrigue, p.Renown, p.Epithets, p.Loyalty);

    private static Proposal.AddBattle.Side SideRecord(WarfareProfile profile, BattleResolver.Side side, BattleResolver.SideResult r)
        => new(side.Polity, side.General?.Person ?? -1, r.Strength, r.Loss, r.Supply, r.Morale,
            r.Tactic >= 0 ? profile.Tactics[r.Tactic].Key : string.Empty, r.Unit);

    /// <summary>A side's dead fall on its settlement nearest the field, at most <see cref="BattleDef.CasualtyCap"/> of its people.</summary>
    private static void Casualties(TurnView view, ProposalSink sink, WarfareProfile profile, int polity, int tile, BattleResolver.SideResult side,
        WarfareProfile.Way? victor)
    {
        if (!TryNearest(view, polity, tile, out var settlement) || !(settlement.Population > 0)) return;
        double dead = side.Strength * side.Loss;
        if (victor != null)
            foreach (var c in victor.Customs) dead *= c.Casualties;
        double share = Math.Min(profile.Data.CasualtyCap, dead / settlement.Population);
        if (share > 0) sink.Emit(new Proposal.KillPopulation { EntityId = polity, Settlement = settlement.Index, Share = share });
    }

    private static bool TryNearest(TurnView view, int polity, int tile, out TurnView.SettlementState nearest)
    {
        var vectors = view.Map.DataStore.GetTileVectors();
        nearest = default;
        if ((uint)tile >= (uint)vectors.Length) return false;
        double best = double.NegativeInfinity;
        bool found = false;
        foreach (var s in view.Settlements)
        {
            if (s.Ended || s.PolityId != polity) continue;
            double dot = Vector3D.Dot(vectors[tile], vectors[s.Tile]);
            if (dot > best) { best = dot; nearest = s; found = true; }
        }
        return found;
    }

    /// <summary>
    /// The outcome's aftermath (<see cref="BattleDef.Aftermath"/>) on the defender's settlement nearest the field. With sieges on,
    /// after a <see cref="BattleDef.Besiege"/> outcome (or a refused field, <see cref="BattleDef.BesiegeDeclined"/>) the place is
    /// besieged instead when its walls can stand one (<see cref="Besiege"/>).
    /// </summary>
    private void Aftermath(TurnView view, ProposalSink sink, HashSet<(int, int)> severedNow, WarfareProfile profile,
        TurnView.PolityState attacker, TurnView.PolityState defender, BattleResolver.Side a, BattleResolver.Side d, int tile,
        BattleResolver.Result result, Proposal.AddBattle battle, int key, bool sieges)
    {
        var data = profile.Data;
        string action = profile.Aftermath(result.Outcome);
        bool besiege = sieges && (result.Declined ? data.BesiegeDeclined && result.AttackerWon
            : data.Besiege.Any(o => string.Equals(o, result.Outcome.ToString(), StringComparison.OrdinalIgnoreCase)));
        if (besiege && TryNearest(view, defender.Id, tile, out var walled)
            && Besiege(view, sink, severedNow, profile, attacker, defender, a, d, walled, result, battle, key))
            return;
        if (action == "grudge")
        {
            if (data.AftermathGrudge > 0) sink.Emit(new Proposal.AddGrudge { EntityId = defender.Id, Other = attacker.Id, Weight = data.AftermathGrudge });
            return;
        }
        if (action is not ("capture" or "raze" or "sack") || !TryNearest(view, defender.Id, tile, out var target)) return;
        Take(view, sink, severedNow, profile, attacker, defender, a.Way, target, action, null);
    }

    /// <summary>Takes <paramref name="target"/>: a capture (razed under a razing custom, sacked over the cap), a raze or a sack; a capital is only sacked.</summary>
    private static void Take(TurnView view, ProposalSink sink, HashSet<(int, int)> severedNow, WarfareProfile profile, TurnView.PolityState attacker,
        TurnView.PolityState defender, WarfareProfile.Way attackerWay, TurnView.SettlementState target, string action, Proposal? requires)
    {
        var data = profile.Data;
        if (action == "capture")
        {
            foreach (var c in attackerWay.Customs) if (c.Raze) action = "raze";
            int standing = 0;
            foreach (var s in view.Settlements) if (!s.Ended && s.PolityId == attacker.Id) standing++;
            if (action == "capture" && standing >= view.MaxSettlementsPerPolity) action = "sack";
        }
        if (target.Role == SettlementRole.Capital) action = "sack";
        switch (action)
        {
            case "capture": Capture(sink, attacker.Id, defender.Id, target, requires); break;
            case "raze": Raze(view, sink, severedNow, attacker.Id, defender.Id, target, data.RazeCasualty, requires); break;
            default: Sack(sink, defender.Id, target, data.SackDisplacement, data.SackCasualty, requires); break;
        }
    }

    /// <summary>Fallen commanders die; the winner gains renown and the loser loses some; commanders who keep meeting become nemeses.</summary>
    private static void Commanders(TurnView view, ProposalSink sink, BattleDef data, BattleResolver.Side a, BattleResolver.Side d, BattleResolver.Result result)
    {
        void Fall(BattleResolver.Side side, BattleResolver.SideResult r)
        {
            if (side.General != null && r.GeneralFell)
                sink.Emit(new Proposal.EndPerson { EntityId = side.Polity, Person = side.General.Person, Cause = data.GeneralDeathCause });
        }
        void Renown(BattleResolver.Side side, int delta)
        {
            if (side.General != null && delta != 0) sink.Emit(new Proposal.AdjustPerson { EntityId = side.Polity, Person = side.General.Person, Renown = delta });
        }
        Fall(a, result.Attacker);
        Fall(d, result.Defender);
        bool decisive = result.Outcome is BattleOutcome.AttackerDecisive or BattleOutcome.DefenderDecisive;
        int win = data.RenownWin + (decisive ? data.RenownDecisive : 0);
        if (result.AttackerWon) { Renown(a, win); Renown(d, data.RenownLoss); }
        else if (result.DefenderWon) { Renown(d, win); Renown(a, data.RenownLoss); }
        if (a.General == null || d.General == null || result.Attacker.GeneralFell || result.Defender.GeneralFell) return;
        int ga = a.General.Person, gd = d.General.Person;
        int met = 1;
        foreach (var b in view.Battles)
            if ((b.GeneralA == ga && b.GeneralD == gd) || (b.GeneralA == gd && b.GeneralD == ga)) met++;
        if (met < data.NemesisMeetings) return;
        foreach (var r in view.RelationsOf(ga))
            if (r.Kind == data.NemesisRelation && (r.A == gd || r.B == gd)) return;
        sink.Emit(new Proposal.AddRelation { EntityId = a.Polity, A = Math.Min(ga, gd), B = Math.Max(ga, gd), RelationKind = data.NemesisRelation });
    }
}
