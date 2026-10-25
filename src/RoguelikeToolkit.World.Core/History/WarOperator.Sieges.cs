using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Settlement;

namespace RoguelikeToolkit.World.Core.History;

public sealed partial class WarOperator
{
    /// <summary>
    /// With <see cref="HistoryFeatures.Sieges"/> on: besieges <paramref name="target"/> after the field battle, when its walls
    /// can stand a siege (<see cref="Peoples.FortificationDef.SiegeWalls"/>); false (and nothing proposed) when they cannot.
    /// The place: its garrison (a share of its people, plus a fort's own), its walls (tier, capital, high ground, the holder's
    /// appetite for walls, a fort's walls), its food (a city's stores, a river or a sea the besiegers cannot close). The
    /// besiegers are the field host after its losses. A relief army marches from the defender's nearest other settlement when its
    /// free levy is large enough. The siege is one <see cref="HistoryBattle"/> (narrated by a <see cref="HistoryEventKind.Siege"/>
    /// event, its parent the field battle), a relief battle another (its parent the siege). What follows waits on the siege
    /// record (<see cref="Proposal.Requires"/>): a fall takes the place (a capital is sacked and pays tribute), a siege that
    /// held leaves the besiegers a grudge.
    /// </summary>
    private bool Besiege(TurnView view, ProposalSink sink, HashSet<(int, int)> severedNow, WarfareProfile profile,
        TurnView.PolityState attacker, TurnView.PolityState defender, BattleResolver.Side a, BattleResolver.Side d,
        TurnView.SettlementState target, BattleResolver.Result field, Proposal.AddBattle fieldBattle, int key)
    {
        var siegeData = profile.Siege;
        var store = view.Map.DataStore;
        var vectors = store.GetTileVectors();
        int tile = target.Tile;
        bool hasElev = store.IsLayerRegistered<ElevationInfo>(), hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        bool hasBiome = store.IsLayerRegistered<BiomeInfo>(), hasClimate = store.IsLayerRegistered<ClimateInfo>();
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;
        var biomes = hasBiome ? store.GetSpan<BiomeInfo>() : default;
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;
        double height = hasElev ? elev[tile].Height : 0;
        bool capital = target.Role == SettlementRole.Capital, fort = target.Role == SettlementRole.Post;
        double walls = profile.Walls(target.Tier, capital, height >= profile.Forts.HighFrom, fort, d.Way);
        if (walls < profile.Forts.SiegeWalls) return false;

        double garrison = target.Population * profile.GarrisonShare(target.Tier) + (fort ? d.Way.Fort?.Garrison ?? 0 : 0);
        bool river = hasHydro && hydro[tile].IsRiver != 0;
        bool coastal = Coastal(store, elev, hasElev, tile);
        bool attackerAtSea = false;
        if (coastal)
            foreach (var s in view.Settlements)
                if (!s.Ended && s.PolityId == attacker.Id && Coastal(store, elev, hasElev, s.Tile)) { attackerAtSea = true; break; }
        int terrain = hasBiome ? profile.TerrainOf(biomes[tile].Biome, height) : profile.DefaultTerrain;
        var place = new SiegeResolver.Place(tile, garrison, walls, target.Tier >= SettlementTier.City, river || (coastal && !attackerAtSea),
            hasBiome && biomes[tile].Biome == BiomeType.Swamp, terrain, field.Season, 0.5, hasClimate ? climate[tile].SeasonalRange : 0);

        // The defender's levy, less the field's dead and the men behind the walls, may march to the rescue from its nearest other
        // settlement.
        double free = Math.Max(0, d.Levy - field.Defender.Strength * field.Defender.Loss - garrison);
        double reliefFrom = double.PositiveInfinity;
        foreach (var s in view.Settlements)
            if (!s.Ended && s.PolityId == defender.Id && s.Index != target.Index)
                reliefFrom = Math.Min(reliefFrom, Degrees(vectors[tile], vectors[s.Tile]));
        var relief = double.IsInfinity(reliefFrom) ? default : new SiegeResolver.Relief(free, reliefFrom);

        double host = field.Attacker.Strength * (1 - field.Attacker.Loss);
        var r = SiegeResolver.Resolve(profile, a, host, field.Attacker.Supply, field.Attacker.Morale, d, place, relief,
            purpose => sink.Rng(key, purpose), view.MaxSiegeMonths, view.TurnYears);
        bool fell = SiegeResolver.Fell(r.End);

        var participants = new List<EventParticipant>();
        if (a.General != null) participants.Add(sink.Participant(a.General.Person, ParticipantRoles.PersonPrefix + "attacker_general"));
        int year = Math.Min(view.TurnYears - 1, field.Year + ((int)field.Season * 3 + r.Months) / 12);
        var siege = new Proposal.AddBattle
        {
            EntityId = attacker.Id,
            EventKind = HistoryEventKind.Siege,
            Tags = new[]
            {
                "siege", $"attacker:{attacker.Id}", $"defender:{defender.Id}", $"settlement:{target.Index}", $"end:{r.End}",
                $"outcome:{OutcomeTag(r.Outcome)}",
            },
            Participants = participants.Count > 0 ? participants : null,
            Year = year, Season = r.Season, BattleKind = BattleKind.Siege, Tile = tile, Terrain = profile.Terrains[terrain],
            Settlement = target.Index,
            Attacker = new Proposal.AddBattle.Side(attacker.Id, a.General?.Person ?? -1, host, r.AttackerLoss, r.AttackerSupply, field.Attacker.Morale,
                r.Tactic >= 0 ? profile.Tactics[r.Tactic].Key : string.Empty),
            Defender = new Proposal.AddBattle.Side(defender.Id, -1, garrison, r.DefenderLoss, 1, r.DefenderMorale),
            Outcome = r.Outcome, Beats = r.Beats, SiegeMonths = r.Months, SiegeEnd = r.End, ParentBattle = fieldBattle,
        };
        sink.Emit(siege);

        if (r.ReliefBattle is { } rb)
        {
            // Proposed by the besiegers' polity, after the siege, so the commit links it to the siege (same entity, later sequence).
            var reliefParts = new List<EventParticipant>();
            if (d.General != null) reliefParts.Add(sink.Participant(d.General.Person, ParticipantRoles.PersonPrefix + "attacker_general"));
            if (a.General != null) reliefParts.Add(sink.Participant(a.General.Person, ParticipantRoles.PersonPrefix + "defender_general"));
            var reliever = d with { Levy = relief.Levy };
            var besiegers = a with { Levy = host };
            sink.Emit(new Proposal.AddBattle
            {
                EntityId = attacker.Id,
                EventKind = rb.Outcome == BattleOutcome.Averted ? HistoryEventKind.Campaign : HistoryEventKind.War,
                Tags = new List<string>
                {
                    rb.Outcome == BattleOutcome.Averted ? "campaign" : "war", "relief", $"attacker:{defender.Id}", $"defender:{attacker.Id}",
                    $"outcome:{OutcomeTag(rb.Outcome)}",
                }.Concat(rb.Outcome == BattleOutcome.Averted ? new[] { rb.Declined ? DeclinedTag : SupplyFailedTag } : Array.Empty<string>()).ToList(),
                Participants = reliefParts.Count > 0 ? reliefParts : null,
                Year = year, Season = rb.Season, BattleKind = rb.Kind, Tile = tile, Terrain = profile.Terrains[rb.Terrain],
                Attacker = SideRecord(profile, reliever, rb.Attacker), Defender = SideRecord(profile, besiegers, rb.Defender),
                Outcome = rb.Outcome, Beats = rb.Beats, ParentBattle = siege,
            });
            Commanders(view, sink, profile.Data, reliever, besiegers, rb);
        }

        // The dead: the garrison's, and a stormed town's, fall on the place; the besiegers' on their own nearest settlement.
        if (target.Population > 0)
        {
            double share = Math.Min(siegeData.CasualtyCap, garrison * r.DefenderLoss / target.Population);
            share = 1 - (1 - share) * (1 - r.Casualty);
            if (share > 0) sink.Emit(new Proposal.KillPopulation { EntityId = defender.Id, Settlement = target.Index, Share = Math.Min(1, share) });
        }
        Casualties(view, sink, profile, attacker.Id, tile, new BattleResolver.SideResult(host, r.AttackerLoss, 0, 0, -1, string.Empty, false, false), null);

        if (fell)
        {
            if (a.General != null && profile.Data.RenownWin != 0)
                sink.Emit(new Proposal.AdjustPerson { EntityId = attacker.Id, Person = a.General.Person, Renown = profile.Data.RenownWin, Requires = siege });
            if (capital && siegeData.TributeTurns > 0)
                sink.Emit(new Proposal.AddWindow { EntityId = defender.Id, Other = attacker.Id, UntilTurn = view.Turn + siegeData.TributeTurns, Requires = siege });
            Take(view, sink, severedNow, profile, attacker, defender, a.Way, target, "capture", siege);
        }
        else
        {
            if (siegeData.FailedGrudge > 0)
                sink.Emit(new Proposal.AddGrudge { EntityId = attacker.Id, Other = defender.Id, Weight = siegeData.FailedGrudge, Requires = siege });
            sink.Adjust(attacker.Id, ScalarRegistry.Pressure, HistoryDemography.WarPressureBump);
        }
        return true;
    }

    private static bool Coastal(Storage.WorldDataStore store, ReadOnlySpan<ElevationInfo> elev, bool hasElev, int tile)
    {
        if (!hasElev) return false;
        Span<int> neighbors = stackalloc int[6];
        int n = store.GetAdjacent(tile, neighbors);
        for (int k = 0; k < n; k++)
            if ((uint)neighbors[k] < (uint)elev.Length && elev[neighbors[k]].Height < 0) return true;
        return false;
    }
}
