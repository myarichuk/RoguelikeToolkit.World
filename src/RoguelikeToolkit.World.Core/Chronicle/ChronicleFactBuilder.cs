using System.Globalization;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// Reads one <see cref="HistoryEvent"/> and the records it points at (its <see cref="HistoryBattle"/>, persons, settlements,
/// routes, the other party) into <see cref="ChronicleFacts"/>. Read-only: it never interns a string, appends an event or
/// touches the catalog's indexes, so rendering leaves <see cref="HistoryCatalog.ToBytes"/> unchanged. It reads only what
/// was true when the event happened (names, epithets, birth, the battle record), so appending to the log never changes how
/// an earlier event reads. Every key it writes is declared in <see cref="ChronicleVocabulary"/>.
/// </summary>
internal sealed class ChronicleFactBuilder
{
    private readonly HistoryCatalog _catalog;
    private readonly IChronicleNameResolver _names;
    private readonly ChronicleOptions _options;
    private Dictionary<int, int>? _settlementAt;

    /// <summary>Builds a fact reader for <paramref name="catalog"/>.</summary>
    public ChronicleFactBuilder(HistoryCatalog catalog, IChronicleNameResolver names, ChronicleOptions options)
    {
        _catalog = catalog;
        _names = names;
        _options = options;
    }

    /// <summary>The grammar symbol an event of <paramref name="kind"/> renders from (<c>event.War</c>...).</summary>
    public static string RootOf(HistoryEventKind kind) => "event." + kind;

    /// <summary>The facts of <paramref name="e"/>.</summary>
    public ChronicleFacts Build(HistoryEvent e)
    {
        var f = new ChronicleFacts();
        int turn = e.Turn;
        f.Set("turn", Num(turn));
        f.Set("year", Num(Year(turn, 0)));
        f.SetLex("what", "what." + e.Kind, ChronicleText.Humanize(e.Kind.ToString()).ToLowerInvariant());
        foreach (var t in e.Tags) f.Add("tag", t);
        if (IsPolity(e.PolityId))
        {
            var p = _catalog.Polities[e.PolityId];
            f.Culture = string.IsNullOrEmpty(p.CultureKey) ? ChronicleData.Common : p.CultureKey;
            Realm(f, string.Empty, e.PolityId);
            f.Set("form", p.Form.ToString());
            f.Set("folk", p.FolkKey);
        }
        int at = SettlementAt(e.TileIndex);
        if (at >= 0) f.Set("place", _names.SettlementName(_catalog, at));

        int who = TagInt(e, "person:");
        if (who >= 0) Person(f, "who", who, turn);
        foreach (var part in e.Participants)
        {
            string role = RoleName(part.Role);
            if (!role.StartsWith(ParticipantRoles.PersonPrefix, StringComparison.Ordinal)) continue;
            string r = role.Substring(ParticipantRoles.PersonPrefix.Length);
            if (r is "outlaw" or "merchant" or "privateer" or "agent" or "spymaster" && !f.Has(r)) Person(f, r, part.EntityId, turn);
        }

        switch (e.Kind)
        {
            case HistoryEventKind.War:
            case HistoryEventKind.Campaign:
            case HistoryEventKind.Siege:
                Battle(f, e);
                break;
            case HistoryEventKind.Conquest:
                Other(f, TagInt(e, "loser:"));
                f.Set("overlay", e.Tags.Count > 0 ? e.Tags[e.Tags.Count - 1] : null);
                break;
            case HistoryEventKind.Rebellion:
                Other(f, TagInt(e, "parent:"));
                break;
            case HistoryEventKind.Treaty:
                foreach (var t in e.Tags)
                    if (t.StartsWith("party:", StringComparison.Ordinal) && int.TryParse(t.Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int party) && party != e.PolityId)
                        Other(f, party);
                f.Set("treaty", HasTag(e, "marriage") ? "marriage" : HasTag(e, "foster") ? "foster" : null);
                break;
            case HistoryEventKind.Grudge:
                Other(f, TagInt(e, "against:"));
                f.Set("refused", TagValue(e, "refused:"));
                break;
            case HistoryEventKind.Tribute:
                Other(f, TagInt(e, "from:"));
                break;
            case HistoryEventKind.Ultimatum:
                Other(f, TagInt(e, "weak:"));
                break;
            case HistoryEventKind.Desecration:
                Other(f, TagInt(e, "holy:"));
                Faith(f, TagInt(e, "holy:"));
                break;
            case HistoryEventKind.HolyFound:
                f.Set("faith", TagValue(e, "faith:"));
                if (f.TryValue("faith", out var form)) f.SetLex("faith.word", "faith." + form, ChronicleText.Humanize(form));
                break;
            case HistoryEventKind.Myth:
                Myth(f, e);
                break;
            case HistoryEventKind.Villain:
                Agenda(f, TagValue(e, "agenda:"));
                break;
            case HistoryEventKind.PolityAbsorbed:
                Other(f, TagInt(e, "into:"));
                break;
            case HistoryEventKind.PolityCollapsed:
            {
                var heirs = new List<string>();
                foreach (var t in e.Tags)
                    if (t.StartsWith("successor:", StringComparison.Ordinal) && int.TryParse(t.Substring(10), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && IsPolity(s))
                        heirs.Add(_names.PolityName(_catalog, s));
                if (heirs.Count > 0) f.Set("successors", ChronicleText.JoinList(heirs));
                f.Set("successor.count", Num(heirs.Count));
                break;
            }
            case HistoryEventKind.SettlementFounded:
                Settlement(f, "settlement", TagInt(e, "settlement:"));
                Settlement(f, "from.settlement", TagInt(e, "from:"));
                f.Set("role", TagValue(e, "role:"));
                f.Set("why", TagValue(e, "why:"));
                if (f.TryValue("role", out var role)) f.SetLex("settlement.kind", "role." + role, ChronicleText.Humanize(role).ToLowerInvariant());
                break;
            case HistoryEventKind.SettlementEnded:
                Settlement(f, "settlement", TagInt(e, "settlement:"));
                f.Set("cause", Bare(e));
                break;
            case HistoryEventKind.SettlementCaptured:
                Settlement(f, "settlement", TagInt(e, "settlement:"));
                Other(f, TagInt(e, "from:"));
                break;
            case HistoryEventKind.SettlementTierChanged:
                Settlement(f, "settlement", TagInt(e, "settlement:"));
                f.Set("dir", HasTag(e, "up") ? "up" : HasTag(e, "down") ? "down" : null);
                Tier(f, "tier.from", TagValue(e, "from:"));
                Tier(f, "tier.to", TagValue(e, "to:"));
                break;
            case HistoryEventKind.Shock:
                Shock(f, e);
                break;
            case HistoryEventKind.RoadBuilt:
            case HistoryEventKind.RoadCut:
            case HistoryEventKind.Exchange:
            case HistoryEventKind.Smuggling:
                Route(f, TagValue(e, "route:"));
                Person(f, "merchant", TagInt(e, "merchant:"), turn);
                if (e.Kind == HistoryEventKind.Smuggling) Person(f, "merchant2", TagIntAt(e, "merchant:", 1), turn);
                break;
            case HistoryEventKind.CaravanRaided:
                Route(f, TagValue(e, "route:"));
                Other(f, TagInt(e, "victim:"));
                Person(f, "merchant", TagInt(e, "merchant:"), turn);
                if (TagValue(e, "band:") is { } band)
                {
                    if (int.TryParse(band, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bandId)) Person(f, "outlaw", bandId, turn);
                    else f.Set("brigands", "yes");
                }
                if (HasTag(e, "brigands") || !f.Has("outlaw")) f.Set("brigands", "yes");
                break;
            case HistoryEventKind.OutlawRose:
                Route(f, TagValue(e, "route:"));
                Other(f, TagInt(e, "from:"));
                f.Set("source", TagValue(e, "source:"));
                Haven(f, TagInt(e, "haven:"));
                if (who >= 0 && !f.Has("outlaw")) Person(f, "outlaw", who, turn);
                break;
            case HistoryEventKind.OutlawEnded:
                Route(f, TagValue(e, "route:"));
                Other(f, TagInt(e, "by:"));
                f.Set("end", TagValue(e, "end:"));
                if (who >= 0 && !f.Has("outlaw")) Person(f, "outlaw", who, turn);
                break;
            case HistoryEventKind.Intrigue:
            {
                // The sponsor is the realm whatever polity the event sits on (an exposure is the victim's event).
                int actor = TagInt(e, "actor:");
                if (IsPolity(actor) && actor != e.PolityId) Realm(f, string.Empty, actor);
                Other(f, TagInt(e, "target:"));
                string plot = TagValue(e, "plot:") ?? string.Empty;
                f.Set("plot", plot);
                Route(f, TagValue(e, "route:"));
                // person: is the band of a sponsorship, the new agent of a spy network, else the victim of an operation.
                if (who >= 0)
                {
                    string holder = plot is "sponsorship" or "sponsorship_exposed" ? "outlaw" : plot == "spy_network" ? "agent" : "victim";
                    if (!f.Has(holder)) Person(f, holder, who, turn);
                }
                Person(f, "agent", TagInt(e, "agent:"), turn);
                Person(f, "outlaw", TagInt(e, "band:"), turn);
                break;
            }
            case HistoryEventKind.Privateering:
            {
                int privateer = TagInt(e, "privateer:");
                if (privateer < 0) privateer = who;
                Person(f, "privateer", privateer, turn);
                int sponsor = TagInt(e, "sponsor:");
                if (IsPolity(sponsor)) f.Set("sponsor", _names.PolityName(_catalog, sponsor));
                Other(f, TagInt(e, "victim:"));
                break;
            }
            case HistoryEventKind.PersonRose:
                f.Set("office", TagValue(e, "office:"));
                if (f.TryValue("office", out var office))
                    f.SetLex("who.office", "office." + office, ChronicleText.Humanize(office));
                Agenda(f, TagValue(e, "agenda:"));
                Settlement(f, "settlement", TagInt(e, "settlement:"));
                break;
            case HistoryEventKind.PersonDied:
                f.Set("office", TagValue(e, "office:"));
                if (f.TryValue("office", out var held))
                    f.SetLex("who.office", "office." + held, ChronicleText.Humanize(held));
                f.Set("cause", Bare(e));
                Reign(f, who);
                break;
            case HistoryEventKind.Fork:
                f.Set("tiles", TagValue(e, "tiles:"));
                break;
            case HistoryEventKind.Plugin:
            {
                string name = e.PluginKind ?? string.Empty;
                int slash = name.IndexOf('/');
                f.Set("plugin", ChronicleText.Humanize(slash >= 0 ? name.Substring(slash + 1) : name));
                break;
            }
        }
        return f;
    }

    private void Battle(ChronicleFacts f, HistoryEvent e)
    {
        int attacker = TagInt(e, "attacker:"), defender = TagInt(e, "defender:");
        f.Set("outcome", TagValue(e, "outcome:"));
        int id = TagInt(e, "battle:");
        HistoryBattle? b = (uint)id < (uint)_catalog.Battles.Count ? _catalog.Battles[id] : null;
        if (b is null)
        {
            Side(f, "att", attacker, null, e.Turn);
            Side(f, "def", defender, null, e.Turn);
            if (e.Kind == HistoryEventKind.Siege)
            {
                Settlement(f, "settlement", TagInt(e, "settlement:"));
                f.Set("end", TagValue(e, "end:"));
            }
            Outcome(f, e.Kind == HistoryEventKind.Campaign ? BattleOutcome.Averted : (BattleOutcome?)null);
            return;
        }
        f.Set("battle", "yes");
        f.Set("kind", b.Kind.ToString());
        f.Set("year", Num(Year(b.Turn, b.Year)));
        string season = b.Season.ToString().ToLowerInvariant();
        f.Set("season", season);
        f.SetLex("season.word", "season." + season, season);
        if (b.Terrain >= 0)
        {
            string terrain = _catalog.Strings.NameOf(b.Terrain);
            f.Set("terrain", terrain);
            f.SetLex("terrain.noun", "terrain." + terrain, ChronicleText.Humanize(terrain));
        }
        foreach (int beat in b.Beats) f.Add("beat", _catalog.Strings.NameOf(beat));
        if (b.Settlement >= 0) Settlement(f, "settlement", b.Settlement);
        else Settlement(f, "settlement", TagInt(e, "settlement:"));
        if (b.SiegeMonths > 0)
        {
            f.Set("months", Num(b.SiegeMonths));
            f.Set("span", Span(b.SiegeMonths));
        }
        if (b.SiegeEnd >= 0) f.Set("end", _catalog.Strings.NameOf(b.SiegeEnd));
        if (b.Parent >= 0 && (uint)b.Parent < (uint)_catalog.Battles.Count && _catalog.Battles[b.Parent].Settlement >= 0)
            Settlement(f, "parent.settlement", _catalog.Battles[b.Parent].Settlement);
        Side(f, "att", b.Attacker.Polity >= 0 ? b.Attacker.Polity : attacker, b.Attacker, b.Turn);
        Side(f, "def", b.Defender.Polity >= 0 ? b.Defender.Polity : defender, b.Defender, b.Turn);
        if (!f.Has("outcome")) f.Set("outcome", OutcomeTag(b.Outcome));
        Outcome(f, b.Kind == BattleKind.Siege ? SiegeOutcome(f) : b.Outcome);
        var winner = f.TryValue("winner", out var w) ? w : "none";
        if (winner == "attacker") { Side(f, "win", b.Attacker.Polity, b.Attacker, b.Turn); Side(f, "lose", b.Defender.Polity, b.Defender, b.Turn); }
        else if (winner == "defender") { Side(f, "win", b.Defender.Polity, b.Defender, b.Turn); Side(f, "lose", b.Attacker.Polity, b.Attacker, b.Turn); }
        Fallen(f, b.Attacker, b.Turn);
        Fallen(f, b.Defender, b.Turn);
    }

    /// <summary>A general the battle killed (died that turn, cause <c>battle</c>): <c>fallen</c> and <c>fallen.realm</c>.</summary>
    private void Fallen(ChronicleFacts f, BattleSide side, int turn)
    {
        if (f.Has("fallen") || (uint)side.General >= (uint)_catalog.Persons.Count) return;
        var p = _catalog.Persons[side.General];
        if (p.DiedTurn != turn || p.DeathCause < 0 || _catalog.Strings.NameOf(p.DeathCause) != "battle") return;
        Person(f, "fallen", side.General, turn);
        if (IsPolity(side.Polity)) f.Set("fallen.realm", _names.PolityName(_catalog, side.Polity));
    }

    /// <summary>A siege's own winner: the attacker when the place fell, the defender when it held.</summary>
    private static BattleOutcome? SiegeOutcome(ChronicleFacts f)
    {
        if (!f.TryValue("end", out var end)) return null;
        return end switch
        {
            "stormed" => BattleOutcome.AttackerDecisive,
            "surrendered" or "betrayed" => BattleOutcome.AttackerVictory,
            "relieved" => BattleOutcome.DefenderVictory,
            "lifted" or "abandoned" => BattleOutcome.DefenderVictory,
            _ => null,
        };
    }

    private static void Outcome(ChronicleFacts f, BattleOutcome? outcome)
    {
        BattleOutcome? o = outcome;
        if (o is null && f.TryValue("outcome", out var tag)) o = FromTag(tag);
        if (o is null) return;
        (string winner, string margin) = o.Value switch
        {
            BattleOutcome.AttackerDecisive => ("attacker", "decisive"),
            BattleOutcome.AttackerVictory => ("attacker", "victory"),
            BattleOutcome.AttackerPyrrhic => ("attacker", "pyrrhic"),
            BattleOutcome.Stalemate => ("none", "stalemate"),
            BattleOutcome.DefenderVictory => ("defender", "victory"),
            BattleOutcome.DefenderDecisive => ("defender", "decisive"),
            _ => ("none", "averted"),
        };
        f.Set("winner", winner);
        f.Set("margin", margin);
    }

    private static BattleOutcome? FromTag(string tag) => tag switch
    {
        "attacker_decisive" => BattleOutcome.AttackerDecisive,
        "attacker_victory" => BattleOutcome.AttackerVictory,
        "attacker_pyrrhic" => BattleOutcome.AttackerPyrrhic,
        "stalemate" => BattleOutcome.Stalemate,
        "defender_victory" => BattleOutcome.DefenderVictory,
        "defender_decisive" => BattleOutcome.DefenderDecisive,
        "averted" => BattleOutcome.Averted,
        _ => null,
    };

    /// <summary>The outcome tag the war operator writes (<c>attacker_decisive</c>...).</summary>
    internal static string OutcomeTag(BattleOutcome o) => o switch
    {
        BattleOutcome.AttackerDecisive => "attacker_decisive",
        BattleOutcome.AttackerVictory => "attacker_victory",
        BattleOutcome.AttackerPyrrhic => "attacker_pyrrhic",
        BattleOutcome.Stalemate => "stalemate",
        BattleOutcome.DefenderVictory => "defender_victory",
        BattleOutcome.DefenderDecisive => "defender_decisive",
        _ => "averted",
    };

    private void Side(ChronicleFacts f, string side, int polity, BattleSide? record, int turn)
    {
        if (!IsPolity(polity)) return;
        Realm(f, side + ".", polity);
        if (record is null) return;
        if (record.General >= 0) Person(f, side + ".general", record.General, turn);
        if (record.Unit >= 0)
        {
            string unit = _catalog.Strings.NameOf(record.Unit);
            f.Set(side + ".unit", unit);
            f.SetLex(side + ".unit", "unit." + unit, ChronicleText.Humanize(unit));
        }
        if (record.Tactic >= 0)
        {
            string tactic = _catalog.Strings.NameOf(record.Tactic);
            f.Set(side + ".tactic", tactic);
            f.SetLex(side + ".tactic", "tactic." + tactic, ChronicleText.Humanize(tactic));
        }
        string loss = record.LossShare < 0.1 ? "light" : record.LossShare < 0.25 ? "heavy" : record.LossShare < 0.5 ? "grievous" : "annihilated";
        f.Set(side + ".loss", loss);
        f.SetLex(side + ".loss", "loss." + loss, loss);
    }

    private void Realm(ChronicleFacts f, string prefix, int polity)
    {
        var p = _catalog.Polities[polity];
        string culture = string.IsNullOrEmpty(p.CultureKey) ? ChronicleData.Common : p.CultureKey;
        f.Set(prefix + "realm", _names.PolityName(_catalog, polity));
        if (_names is IChronicleShortNames shortNames) f.SetShort(prefix + "realm", shortNames.PolityShortName(_catalog, polity));
        f.Set(prefix + "culture", p.CultureKey);
        f.SetLex(prefix + "people", "people." + p.FolkKey, ChronicleText.Humanize(p.FolkKey) + " folk");
        f.SetLex(prefix + "adj", "adj." + p.FolkKey, ChronicleText.Humanize(p.FolkKey));
        f.SetLex(prefix + "host", "host", "host", culture);
        f.SetLex(prefix + "cry", "cry", "Onward!", culture);
    }

    private void Other(ChronicleFacts f, int polity)
    {
        if (IsPolity(polity)) Realm(f, "other.", polity);
    }

    private void Person(ChronicleFacts f, string prefix, int id, int turn)
    {
        if ((uint)id >= (uint)_catalog.Persons.Count) return;
        var p = _catalog.Persons[id];
        f.Set(prefix, _names.PersonName(_catalog, id));
        f.Set(prefix + ".he", p.Feminine ? "she" : "he");
        f.Set(prefix + ".his", p.Feminine ? "her" : "his");
        f.Set(prefix + ".him", p.Feminine ? "her" : "him");
        if (p.Feminine) f.Set(prefix + ".feminine", "yes");
        if (p.Epithets.Count > 0)
        {
            string epithet = _catalog.Strings.NameOf(p.Epithets[0]);
            f.Set(prefix + ".epithet", epithet);
            f.SetLex(prefix + ".epithet", "epithet." + epithet, "the " + ChronicleText.Capitalize(ChronicleText.Humanize(epithet)));
        }
        int born = p.BornTurn * _options.TurnYears + p.BornYear;
        int age = turn * _options.TurnYears - born;
        if (age > 0) f.Set(prefix + ".age", Num(age));
        if (prefix == "who" && (uint)p.Parent < (uint)_catalog.Persons.Count) f.Set("who.parent", _names.PersonName(_catalog, p.Parent));
        if (prefix != "who")
        {
            string office = OfficeAt(id, turn);
            if (office.Length > 0)
            {
                f.Set(prefix + ".office", office);
                f.SetLex(prefix + ".office", "office." + office, ChronicleText.Humanize(office));
            }
        }
    }

    /// <summary>The office <paramref name="person"/> held at <paramref name="turn"/> (the latest begun by then), or empty.</summary>
    private string OfficeAt(int person, int turn)
    {
        string office = string.Empty;
        foreach (var t in _catalog.Tenures)
            if (t.Person == person && t.FromTurn <= turn && (t.ToTurn < 0 || t.ToTurn >= turn)) office = _catalog.Strings.NameOf(t.Office);
        return office;
    }

    private void Reign(ChronicleFacts f, int person)
    {
        if (person < 0) return;
        foreach (var t in _catalog.Tenures)
        {
            if (t.Person != person || t.ToTurn < 0) continue;
            int years = (t.ToTurn - t.FromTurn) * _options.TurnYears;
            if (years > 0) f.Set("who.reign", Num(years));
        }
    }

    private void Settlement(ChronicleFacts f, string key, int index)
    {
        if ((uint)index >= (uint)_catalog.Settlements.Count) return;
        f.Set(key, _names.SettlementName(_catalog, index));
        if (key == "settlement" && !f.Has("settlement.kind"))
        {
            var s = _catalog.Settlements[index];
            string role = s.Role.ToString();
            f.SetLex("settlement.kind", s.Role == SettlementRole.Capital ? "tier." + s.Tier : "role." + role,
                ChronicleText.Humanize(role).ToLowerInvariant());
        }
    }

    private static void Tier(ChronicleFacts f, string key, string? tier)
    {
        if (string.IsNullOrEmpty(tier)) return;
        f.Set(key, tier);
        f.SetLex(key, "tier." + tier, tier!.ToLowerInvariant());
    }

    private void Route(ChronicleFacts f, string? route)
    {
        if (string.IsNullOrEmpty(route)) return;
        int dash = route!.IndexOf('-');
        if (dash <= 0) return;
        if (!int.TryParse(route.Substring(0, dash), NumberStyles.Integer, CultureInfo.InvariantCulture, out int a)
            || !int.TryParse(route.Substring(dash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)) return;
        string na = PlaceName(a), nb = PlaceName(b);
        if (na.Length == 0 || nb.Length == 0) return;
        f.Set("route.a", na);
        f.Set("route.b", nb);
    }

    private void Haven(ChronicleFacts f, int tile)
    {
        if (tile < 0) return;
        string name = PlaceName(tile);
        if (name.Length > 0) f.Set("haven", name);
    }

    /// <summary>The settlement at <paramref name="tile"/>, else the lands of its owner, else empty.</summary>
    private string PlaceName(int tile)
    {
        int s = SettlementAt(tile);
        if (s >= 0) return _names.SettlementName(_catalog, s);
        int owner = _catalog.OwnerOf(tile);
        return IsPolity(owner) ? "the lands of " + _names.PolityName(_catalog, owner) : string.Empty;
    }

    private void Faith(ChronicleFacts f, int polity)
    {
        foreach (var faith in _catalog.Faiths)
        {
            if (faith.PolityId != polity || string.IsNullOrEmpty(faith.Form)) continue;
            f.Set("faith", faith.Form);
            f.SetLex("faith.word", "faith." + faith.Form, ChronicleText.Humanize(faith.Form));
            return;
        }
    }

    private static void Myth(ChronicleFacts f, HistoryEvent e)
    {
        foreach (var t in e.Tags)
        {
            if (t.StartsWith("faith:", StringComparison.Ordinal)) f.Set("faith", t.Substring(6));
            else if (t.StartsWith("power:", StringComparison.Ordinal)) f.Add("power", t.Substring(6));
            else if (t.StartsWith("origin:", StringComparison.Ordinal)) f.Set("origin", t.Substring(7));
            else if (t.StartsWith("trauma:", StringComparison.Ordinal) && !f.Has("trauma"))
            {
                var parts = t.Split(':');
                if (parts.Length > 1) f.Set("trauma", parts[1]);
            }
        }
        if (f.TryValue("faith", out var form)) f.SetLex("faith.word", "faith." + form, ChronicleText.Humanize(form));
        var powers = f.Values("power");
        // The first two powers are always harvest and hearth; the last ones are what this realm lived through.
        if (powers.Count > 0) f.SetLex("power.word", "power." + powers[powers.Count - 1], powers[powers.Count - 1]);
        if (powers.Count > 1) f.SetLex("power.second", "power." + powers[powers.Count - 2], powers[powers.Count - 2]);
        if (f.TryValue("origin", out var origin)) f.SetLex("origin.people", "people." + origin, ChronicleText.Humanize(origin) + " folk");
    }

    private static void Agenda(ChronicleFacts f, string? agenda)
    {
        if (string.IsNullOrEmpty(agenda)) return;
        f.Set("agenda", agenda);
        f.SetLex("agenda.word", "agenda." + agenda, agenda!);
    }

    private static void Shock(ChronicleFacts f, HistoryEvent e)
    {
        string? kind = TagValue(e, "shock:");
        if (string.IsNullOrEmpty(kind)) return;
        f.Set("shock", kind);
        f.SetLex("shock.word", "shock." + kind, ChronicleText.Humanize(kind!));
        if (double.TryParse(TagValue(e, "severity:"), NumberStyles.Float, CultureInfo.InvariantCulture, out double sev))
            f.Set("severity", sev < 0.45 ? "mild" : sev < 0.7 ? "grave" : "dire");
        int struck = TagInt(e, "struck:");
        if (struck > 0) f.Set("struck", Num(struck));
    }

    private int Year(int turn, int yearInTurn) => _options.FirstYear + turn * _options.TurnYears + yearInTurn;

    private static string Span(int months)
    {
        if (months <= 1) return "a single month";
        if (months < 12) return ChronicleText.Number(Num(months)) + " months";
        if (months == 12) return "a full year";
        if (months < 18) return "a year and more";
        if (months < 24) return "the better part of two years";
        int years = months / 12;
        return ChronicleText.Number(Num(years)) + " long years";
    }

    private int SettlementAt(int tile)
    {
        if (tile < 0) return -1;
        if (_settlementAt is null)
        {
            _settlementAt = new Dictionary<int, int>();
            for (int i = 0; i < _catalog.Settlements.Count; i++)
                if (!_settlementAt.ContainsKey(_catalog.Settlements[i].TileIndex)) _settlementAt[_catalog.Settlements[i].TileIndex] = i;
        }
        return _settlementAt.TryGetValue(tile, out int s) ? s : -1;
    }

    private string RoleName(int role) => (uint)role < (uint)_catalog.Strings.Count ? _catalog.Strings.NameOf(role) : string.Empty;

    private bool IsPolity(int id) => (uint)id < (uint)_catalog.Polities.Count;

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>The first tag that is neither <c>key:value</c> nor a known marker: the cause of a death or a settlement's end.</summary>
    private static string? Bare(HistoryEvent e)
    {
        foreach (var t in e.Tags)
            if (t.IndexOf(':') < 0) return t;
        return null;
    }

    /// <summary>True when <paramref name="e"/> carries the bare tag <paramref name="tag"/>.</summary>
    internal static bool HasTag(HistoryEvent e, string tag)
    {
        foreach (var t in e.Tags)
            if (string.Equals(t, tag, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>The value of the first tag starting with <paramref name="prefix"/>, or null.</summary>
    internal static string? TagValue(HistoryEvent e, string prefix)
    {
        foreach (var t in e.Tags)
            if (t.StartsWith(prefix, StringComparison.Ordinal)) return t.Substring(prefix.Length);
        return null;
    }

    /// <summary>The integer value of the <paramref name="nth"/> (0-based) tag starting with <paramref name="prefix"/> that parses, or -1.</summary>
    internal static int TagIntAt(HistoryEvent e, string prefix, int nth)
    {
        foreach (var t in e.Tags)
            if (t.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(t.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                && nth-- == 0)
                return v;
        return -1;
    }

    /// <summary>The integer value of the first tag starting with <paramref name="prefix"/> that parses, or -1.</summary>
    internal static int TagInt(HistoryEvent e, string prefix)
    {
        foreach (var t in e.Tags)
            if (t.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(t.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
        return -1;
    }
}
