using RoguelikeToolkit.World.Core.Chronicle;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// A hand-built catalog for the chronicle tests: one polity per shipped culture, persons, settlements, battles and sieges,
/// and events of every <see cref="HistoryEventKind"/> with the tag variants each operator writes, including the X6 kinds
/// (caravans, outlaws, intrigue, smuggling, privateers) no operator emits yet. Every polity acts in every kind, so every
/// culture overlay renders every kind.
/// </summary>
internal static class ChronicleSynthetic
{
    private static readonly string[] Terrains = { "open", "forest", "hills", "mountain", "marsh", "desert", "snow" };
    private static readonly string[] Tactics = { "ambush", "berserk_rush", "heavy_charge", "horse_archers", "massed_archery", "pike_square", "shieldwall", "siege_engines", "skirmish", "war_beasts" };
    private static readonly string[] Units = { "knights", "ironbreakers", "wolf_riders", "silver_archers", "men_at_arms", "pony_scouts", "drakes", "snapjaws" };
    private static readonly string[] FieldBeats =
    {
        "flank", "broke_the_line", "feigned_retreat", "arrow_storm", "shields_locked", "pikes_held", "harried", "trap_sprung", "night_attack",
        "frenzy", "stampede", "rain_on_bowstrings", "river_ford_held", "forded_upstream", "snowbound", "mired", "champion_duel", "held_the_line",
        "last_stand", "slaughter", "hard_fought", "rout", "field_refused",
    };
    private static readonly string[] Epithets = { "bold", "cruel", "wise", "cunning", "pious", "craven", "just", "silver_tongued" };
    private static readonly string[] Offices = { "chancellor", "marshal", "spymaster", "treasurer", "admiral", "council", "war_chief", "seer", "elder", "war_leader" };
    private static readonly string[] Causes = { "age", "battle", "accident", "childbed", "fever", "hunting", "illness", "poison", "overthrown", "exile", "executed", "recalled", "assassinated", "hanged", "ruined" };
    private static readonly string[] Shocks = { "earthquake", "plague", "famine", "drought", "flood", "tsunami", "volcano" };
    private static readonly string[] Faiths = { "ancestor", "animism", "polytheism", "war_cult", "dark_cult" };

    /// <summary>Builds the catalog; <paramref name="rounds"/> repeats the event set with shifted variants.</summary>
    public static HistoryCatalog Build(int seed = 77, int rounds = 4)
    {
        var c = new HistoryCatalog(seed);
        var cultures = ChronicleVocabulary.Cultures;
        int n = cultures.Count;
        var forms = new[] { PolityForm.Kingdom, PolityForm.MerchantRepublic, PolityForm.LooseAssociation, PolityForm.Horde };
        for (int i = 0; i < n; i++)
        {
            string culture = cultures[i];
            c.Polities.Add(new Polity { Id = i, Form = forms[i % 4], CapitalTile = 100 + i * 10, FolkKey = ChronicleVocabulary.FolkOf[culture], CultureKey = culture });
            c.Settlements.Add(new HistorySettlement { TileIndex = 100 + i * 10, PolityId = i, Tier = SettlementTier.City, Role = SettlementRole.Capital });
            c.Settlements.Add(new HistorySettlement { TileIndex = 101 + i * 10, PolityId = i, Tier = SettlementTier.Town, Role = SettlementRole.Colony });
            c.Settlements.Add(new HistorySettlement { TileIndex = 102 + i * 10, PolityId = i, Tier = SettlementTier.Village, Role = SettlementRole.Post });
            c.Faiths.Add(new HistoryFaith { PolityId = i, Form = Faiths[i % Faiths.Length] });
        }
        // Persons: per polity a ruler, a general, an official, a merchant, an outlaw and a privateer.
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < 6; k++)
            {
                int id = c.Persons.Count;
                var p = new HistoryPerson
                {
                    Id = id, Kind = (PersonKind)(k == 3 ? 5 : k == 4 ? 7 : k == 5 ? 8 : k), FolkKey = c.Polities[i].FolkKey, CultureKey = c.Polities[i].CultureKey,
                    PolityId = i, Feminine = (id % 3) == 1, BornTurn = -4 + (id % 3), BornYear = id % 10, StableKey = 1000 + id,
                    Parent = id > 6 ? id - 6 : -1,
                };
                if (id % 2 == 0) p.Epithets.Add(c.Strings.Intern(Epithets[id % Epithets.Length]));
                c.Persons.Add(p);
            }
            c.Tenures.Add(new HistoryTenure { Person = i * 6, Polity = i, Office = c.Strings.Intern("ruler"), FromTurn = 0, ToTurn = 6 });
            c.Tenures.Add(new HistoryTenure { Person = i * 6 + 1, Polity = i, Office = c.Strings.Intern("marshal"), FromTurn = 0 });
        }

        int turn = 1;
        for (int round = 0; round < rounds; round++)
        {
            for (int i = 0; i < n; i++, turn++)
            {
                int other = (i + 1 + round) % n;
                int ruler = i * 6, general = i * 6 + 1, official = i * 6 + 2, merchant = i * 6 + 3, outlaw = i * 6 + 4, privateer = i * 6 + 5;
                int otherGeneral = other * 6 + 1;
                int capTile = 100 + i * 10, colony = i * 3 + 1, post = i * 3 + 2, otherCap = other * 3;
                string route = $"route:{capTile}-{100 + other * 10}";
                int v = round * n + i;

                Add(c, HistoryEventKind.Founding, turn, i, capTile, c.Polities[i].Form.ToString(), c.Polities[i].FolkKey, c.Polities[i].CultureKey);
                // Field battles across outcomes, seasons, terrains, kinds and beats.
                for (int o = 0; o < 6; o++)
                {
                    var kind = o == 2 && v % 3 == 0 ? BattleKind.Ambush : o == 4 && v % 4 == 0 ? BattleKind.Naval : BattleKind.Field;
                    var beats = new List<string> { FieldBeats[(v * 6 + o) % FieldBeats.Length] };
                    if ((v + o) % 3 == 0) beats.Add("general_fell");
                    if ((v + o) % 5 == 0) beats.Add("general_captured");
                    if ((v + o) % 4 == 0) beats.Add("heads_taken");
                    if ((v + o) % 6 == 5) beats.Add("turncoat");
                    Battle(c, HistoryEventKind.War, turn, i, other, general, otherGeneral, (BattleOutcome)o, kind, v + o, beats, -1, -1, -1, (v + o) % 7 == 0 ? "winter_quarters" : (v + o) % 5 == 1 ? "mercenaries" : null);
                }
                // A war from the legacy simulation (no battle record) and a siege whose record the log lost.
                Add(c, HistoryEventKind.War, turn, i, capTile, "war", $"attacker:{i}", $"defender:{other}");
                Add(c, HistoryEventKind.Siege, turn, i, capTile, "siege", $"settlement:{otherCap}", "end:lifted", $"attacker:{i}", $"defender:{other}");
                // A relief battle and sieges with every end.
                var ends = new[] { "stormed", "surrendered", "betrayed", "relieved", "lifted", "abandoned" };
                var endBeats = new[] { "escalade", "starved_out", "gate_opened", "relief_arrived", "camp_fever", "held_out" };
                for (int s = 0; s < ends.Length; s++)
                {
                    int siege = Battle(c, HistoryEventKind.Siege, turn, i, other, general, otherGeneral,
                        s < 3 ? BattleOutcome.AttackerVictory : BattleOutcome.DefenderVictory, BattleKind.Siege, v + s,
                        new List<string> { endBeats[s], s == 4 && v % 2 == 0 ? "winter_camp" : "walls_breached" }, otherCap, 2 + (v + s) * 3 % 30, -1, null, ends[s]);
                    if (s == 3) Battle(c, HistoryEventKind.War, turn, other, i, otherGeneral, general, BattleOutcome.AttackerVictory, BattleKind.Relief, v, new List<string> { "flank" }, -1, -1, siege, null);
                }
                // Campaigns that never fought.
                Battle(c, HistoryEventKind.Campaign, turn, i, other, general, otherGeneral, BattleOutcome.Averted, BattleKind.Field, v, new List<string> { "withdrew" }, -1, -1, -1, "declined");
                Battle(c, HistoryEventKind.Campaign, turn, i, other, general, -1, BattleOutcome.Averted, BattleKind.Field, v + 1, new List<string>(), -1, -1, -1, "supply_failed");

                // Court.
                string[] riseTags = { "founder", "succession", "regency", "kinsman", "accession", "villain" };
                string riseTag = riseTags[v % riseTags.Length];
                var rose = new List<string> { $"person:{ruler}", "office:ruler", riseTag };
                if (riseTag == "villain") rose.Add("agenda:shadow");
                Add(c, HistoryEventKind.PersonRose, turn, i, capTile, rose.ToArray()).Participants.Add(Role(c, ruler, "person:ruler"));
                Add(c, HistoryEventKind.PersonRose, turn, i, capTile, $"person:{official}", $"office:{Offices[v % Offices.Length]}");
                Add(c, HistoryEventKind.PersonRose, turn, i, capTile, $"person:{general}", $"office:{Offices[(v + 3) % Offices.Length]}", "favourite");
                Add(c, HistoryEventKind.PersonRose, turn, i, capTile, $"person:{merchant}", "merchant", $"settlement:{colony}");
                Add(c, HistoryEventKind.PersonDied, turn, i, capTile, $"person:{ruler}", Causes[v % Causes.Length], "office:ruler");
                Add(c, HistoryEventKind.PersonDied, turn, i, capTile, $"person:{official}", Causes[(v + 5) % Causes.Length]);
                Add(c, HistoryEventKind.Villain, turn, i, capTile, "villain", "villain:0", $"agenda:{new[] { "dominion", "ruin", "hunger", "shadow" }[v % 4]}", $"folk:{c.Polities[i].FolkKey}");
                Add(c, HistoryEventKind.Prophecy, turn, i, capTile, $"person:{official}");

                // Diplomacy.
                Add(c, HistoryEventKind.Treaty, turn, i, capTile, "treaty", v % 2 == 0 ? "marriage" : "foster", $"party:{i}", $"party:{other}");
                Add(c, HistoryEventKind.Grudge, turn, i, capTile, "grudge", $"against:{other}", $"refused:{(v % 2 == 0 ? "foster" : "marriage")}", "weight:2");
                Add(c, HistoryEventKind.Ultimatum, turn, i, capTile, "ultimatum", v % 2 == 0 ? "resist" : "submit", $"khan:{i}", $"weak:{other}");
                Add(c, HistoryEventKind.Tribute, turn, i, capTile, "tribute", $"from:{other}", $"to:{i}");
                Add(c, HistoryEventKind.Conquest, turn, i, capTile, "conquest", $"winner:{i}", $"loser:{other}", c.Polities[i].CultureKey);
                Add(c, HistoryEventKind.Rebellion, turn, i, capTile, "rebellion", $"parent:{other}", c.Polities[i].FolkKey, c.Polities[i].CultureKey);
                Add(c, HistoryEventKind.PolityCollapsed, turn, i, capTile, "collapse", $"successor:{other}", $"successor:{(other + 1) % n}");
                Add(c, HistoryEventKind.PolityAbsorbed, turn, i, capTile, "absorbed", $"into:{other}");

                // Faith.
                Add(c, HistoryEventKind.HolyFound, turn, i, capTile, "holy", $"faith:{Faiths[v % Faiths.Length]}", $"tile:{capTile}");
                Add(c, HistoryEventKind.Desecration, turn, i, capTile, "desecration", $"by:{i}", $"holy:{other}", $"tile:{capTile}");
                Add(c, HistoryEventKind.Myth, turn, i, capTile, "myth", $"faith:{Faiths[v % Faiths.Length]}", $"origin:{c.Polities[i].FolkKey}",
                    "power:harvest", "power:hearth", v % 2 == 0 ? "power:war" : "power:peace", $"trauma:{new[] { "war", "conquest", "desecration" }[v % 3]}:{turn}");

                // Calamity and settlements.
                Add(c, HistoryEventKind.Shock, turn, i, capTile, $"shock:{Shocks[v % Shocks.Length]}", $"severity:{0.3 + (v % 7) * 0.1:0.00}", $"struck:{v % 4}");
                string[] roles = { "Colony", "Post", "Waystation", "Shrine", "Refuge", "Node" };
                string[] whys = { "", "fort", "waystation", "pilgrimage", "refuge", "route" };
                var founded = new List<string> { $"settlement:{colony}", $"role:{roles[v % roles.Length]}", $"from:{i * 3}" };
                if (whys[v % whys.Length].Length > 0) founded.Add($"why:{whys[v % whys.Length]}");
                Add(c, HistoryEventKind.SettlementFounded, turn, i, 101 + i * 10, founded.ToArray());
                Add(c, HistoryEventKind.SettlementEnded, turn, i, 102 + i * 10, $"settlement:{post}", new[] { "razed", "earthquake", "plague", "flood", "abandoned", "famine" }[v % 6]);
                Add(c, HistoryEventKind.SettlementCaptured, turn, i, 100 + other * 10, $"settlement:{otherCap}", $"from:{other}", $"to:{i}");
                Add(c, HistoryEventKind.SettlementTierChanged, turn, i, 101 + i * 10, $"settlement:{colony}", v % 2 == 0 ? "up" : "down",
                    v % 2 == 0 ? "from:Town" : "from:City", v % 2 == 0 ? "to:City" : "to:Town");
                Add(c, HistoryEventKind.SiteLost, turn, i, capTile, v % 2 == 0 ? "settlement" : "holy");

                // Roads and trade.
                Add(c, HistoryEventKind.RoadBuilt, turn, i, capTile, "road", "built", route, v % 2 == 0 ? "spur" : "trunk");
                Add(c, HistoryEventKind.RoadCut, turn, i, capTile, "road", "cut", route);
                Add(c, HistoryEventKind.Exchange, turn, i, capTile, "exchange", "loanword", route);

                // X6 kinds (hand-built: no operator writes them yet).
                var raid = Add(c, HistoryEventKind.CaravanRaided, turn, i, capTile, route, v % 2 == 0 ? $"band:{outlaw}" : "band:brigands", $"victim:{other}", $"merchant:{merchant}");
                raid.Participants.Add(Role(c, outlaw, "person:outlaw"));
                raid.Participants.Add(Role(c, merchant, "person:merchant"));
                Add(c, HistoryEventKind.OutlawRose, turn, i, capTile, $"person:{outlaw}", $"source:{new[] { "displaced", "deserters", "dispossessed", "ruined" }[v % 4]}", $"haven:{101 + i * 10}", route, $"from:{other}")
                    .Participants.Add(Role(c, outlaw, "person:outlaw"));
                Add(c, HistoryEventKind.OutlawEnded, turn, i, capTile, $"person:{outlaw}", $"end:{new[] { "hanged", "pardoned", "age" }[v % 3]}", $"by:{other}", route);
                Intrigue(c, turn, i, other, capTile, route, v, outlaw, official, otherGeneral, other * 6 + 3);
                Add(c, HistoryEventKind.Smuggling, turn, i, capTile, route, $"merchant:{merchant}", $"merchant:{other * 6 + 3}");
                if (v % 2 == 0) Add(c, HistoryEventKind.Privateering, turn, i, capTile, $"person:{privateer}", $"sponsor:{i}", $"victim:{other}", "commission");
                else Add(c, HistoryEventKind.Privateering, turn, i, capTile, $"privateer:{privateer}", $"sponsor:{i}", $"victim:{other}", "prize", $"merchant:{other * 6 + 3}");
                Add(c, HistoryEventKind.PersonRose, turn, i, capTile, $"person:{merchant}", "merchant", "office:council");

                // Map edits and plugins.
                Add(c, HistoryEventKind.Fork, turn, -1, -1, $"tiles:{v + 3}");
                c.AppendLinked(HistoryEventKind.Plugin, turn, i, capTile, new[] { "x" }, -1, null, v % 2 == 0 ? "acme/comet_sighting" : "acme/great_fair");
            }
        }
        return c;
    }

    /// <summary>An intrigue event with the tag shapes the X6 operators write (plot, secret/exposed/caught, agent or band).</summary>
    private static void Intrigue(HistoryCatalog c, int turn, int actor, int target, int tile, string route, int v, int band, int agent, int victim, int mark)
    {
        var tags = new List<string> { $"actor:{actor}", $"target:{target}" };
        switch (v % 9)
        {
            case 0: tags.AddRange(new[] { "plot:sponsorship", $"person:{band}", route, "secret" }); if (v % 2 == 0) tags.Add("hired"); break;
            case 1: tags.AddRange(new[] { "plot:spy_network", $"person:{agent}", "secret" }); break;
            case 2: tags.AddRange(new[] { "plot:sponsorship_exposed", $"person:{band}", route, v % 2 == 0 ? "raid" : "confession" }); break;
            case 3: tags.AddRange(new[] { "plot:sabotage", $"agent:{agent}", route, v % 2 == 0 ? "secret" : "exposed" }); break;
            case 4: tags.AddRange(new[] { "plot:sabotage", $"band:{band}", route, "secret" }); break;
            case 5: tags.AddRange(new[] { "plot:bribe", $"agent:{agent}", $"person:{victim}", "secret" }); break;
            case 6: tags.AddRange(new[] { "plot:assassination", $"agent:{agent}", $"person:{victim}", v % 2 == 0 ? "killed" : "failed", "secret" }); break;
            case 7: tags.AddRange(new[] { "plot:theft", $"agent:{agent}", $"person:{mark}", "secret" }); break;
            default: tags.AddRange(new[] { "plot:discovered", $"agent:{agent}" }); break;
        }
        // An agent's operation (3, 5, 6, 7) is caught one time in four; an idle agent is only ever reported when uncovered.
        bool agentOp = v % 9 is 3 or 5 or 6 or 7;
        if ((agentOp && v % 4 == 3) || v % 9 == 8)
        {
            tags.Remove("secret");
            tags.Remove("exposed");
            tags.Add("exposed");
            tags.Add("caught");
        }
        var e = Add(c, HistoryEventKind.Intrigue, turn, actor, tile, tags.ToArray());
        e.Participants.Add(Role(c, agent, "person:agent"));
    }

    private static HistoryEvent Add(HistoryCatalog c, HistoryEventKind kind, int turn, int polity, int tile, params string[] tags)
    {
        c.AppendPriorityEvent(kind, turn, polity, tile, tags);
        return c.Events[^1];
    }

    private static EventParticipant Role(HistoryCatalog c, int entity, string role) => new(entity, c.Strings.Intern(role));

    private static int Battle(HistoryCatalog c, HistoryEventKind kind, int turn, int attacker, int defender, int attGeneral, int defGeneral,
        BattleOutcome outcome, BattleKind battleKind, int variant, List<string> beats, int settlement, int months, int parent, string? tag, string? end = null)
    {
        int id = c.Battles.Count;
        if (beats.Contains("general_fell") && defGeneral >= 0)
        {
            // A fresh defender general who dies on this field, so the fate rules can name the fallen.
            var old = c.Persons[defGeneral];
            defGeneral = c.Persons.Count;
            var fallen = new HistoryPerson
            {
                Id = defGeneral, Kind = PersonKind.General, FolkKey = old.FolkKey, CultureKey = old.CultureKey, PolityId = defender,
                BornTurn = turn - 4, StableKey = 50_000 + defGeneral, DiedTurn = turn, DeathCause = c.Strings.Intern("battle"),
            };
            c.Persons.Add(fallen);
        }
        var b = new HistoryBattle
        {
            Id = id, Turn = turn, Year = variant % 10, Season = (HistorySeason)(variant % 4), Kind = battleKind, Tile = 100 + defender * 10,
            Terrain = c.Strings.Intern(Terrains[variant % Terrains.Length]), Settlement = settlement, Outcome = outcome,
            Attacker = new BattleSide
            {
                Polity = attacker, General = attGeneral, Tactic = c.Strings.Intern(Tactics[variant % Tactics.Length]),
                Unit = c.Strings.Intern(Units[variant % Units.Length]), LossShare = 0.05 + (variant % 6) * 0.1,
            },
            Defender = new BattleSide
            {
                Polity = defender, General = defGeneral, Tactic = c.Strings.Intern(Tactics[(variant + 3) % Tactics.Length]),
                Unit = c.Strings.Intern(Units[(variant + 2) % Units.Length]), LossShare = 0.08 + (variant % 5) * 0.12,
            },
            SiegeMonths = months > 0 ? months : 0, SiegeEnd = end is null ? -1 : c.Strings.Intern(end), Parent = parent,
        };
        foreach (var beat in beats) b.Beats.Add(c.Strings.Intern(beat));
        c.Battles.Add(b);
        var tags = new List<string> { kind == HistoryEventKind.Campaign ? "campaign" : kind == HistoryEventKind.Siege ? "siege" : "war", $"attacker:{attacker}", $"defender:{defender}",
            $"outcome:{ChronicleFactBuilder.OutcomeTag(outcome)}", $"battle:{id}" };
        if (settlement >= 0) tags.Add($"settlement:{settlement}");
        if (end != null) tags.Add($"end:{end}");
        if (tag != null) tags.Add(tag);
        var e = Add(c, kind, turn, attacker, b.Tile, tags.ToArray());
        if (attGeneral >= 0) e.Participants.Add(Role(c, attGeneral, "person:attacker_general"));
        if (defGeneral >= 0) e.Participants.Add(Role(c, defGeneral, "person:defender_general"));
        b.EventId = e.Id;
        return id;
    }
}
