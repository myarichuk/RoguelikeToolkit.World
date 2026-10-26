using System.Runtime.InteropServices.JavaScript;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.MapEditor.Engine;

/// <summary>
/// History expansion exports (docs/HISTORY_EXPANSION_PLAN.md §7): persons, their bonds and offices, battle records and route
/// states, one primitive array per call with the strides documented on each (the layout tests pin them). Fields that name a
/// thing hold an id into <see cref="HistoryStrings"/> (-1 for none); shares are scaled to integers (×1000, wealth ×100).
/// </summary>
public static partial class WorldExports
{
    /// <summary>Stride of <see cref="HistoryPersons"/>.</summary>
    public const int PersonStride = 25;
    /// <summary>Stride of <see cref="HistoryRelations"/>.</summary>
    public const int RelationStride = 6;
    /// <summary>Stride of <see cref="HistoryTenures"/>.</summary>
    public const int TenureStride = 5;
    /// <summary>Stride of <see cref="HistoryBattles"/>.</summary>
    public const int BattleStride = 24;
    /// <summary>Stride of <see cref="HistoryRouteStates"/>.</summary>
    public const int RouteStateStride = 10;
    /// <summary>Stride of <see cref="HistoryRouteSeries"/>.</summary>
    public const int RouteSampleStride = 5;

    /// <summary>Names of <see cref="PersonKind"/> indexed by value (the <c>kind</c> field of <see cref="HistoryPersons"/>).</summary>
    [JSExport]
    public static string[] HistoryPersonKindNames() => EnumNames<PersonKind>();

    /// <summary>Names of <see cref="BattleKind"/> indexed by value.</summary>
    [JSExport]
    public static string[] HistoryBattleKindNames() => EnumNames<BattleKind>();

    /// <summary>Names of <see cref="BattleOutcome"/> indexed by value.</summary>
    [JSExport]
    public static string[] HistoryBattleOutcomeNames() => EnumNames<BattleOutcome>();

    private static string[] EnumNames<T>() where T : struct, Enum
    {
        var values = (T[])Enum.GetValues(typeof(T));
        var result = new string[values.Max(v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)) + 1];
        Array.Fill(result, string.Empty);
        foreach (var v in values) result[Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)] = v.ToString();
        return result;
    }

    /// <summary>
    /// Per person (<see cref="PersonStride"/> ints): id, kind (<see cref="HistoryPersonKindNames"/>), polity (-1 free), seat settlement
    /// (-1), feminine (0/1), house, parent, born turn, born year, died turn (-1 living), death cause (string id or -1), renown,
    /// wealth ×100, loyalty, pledged ×100, martial, intrigue, stewardship, charisma, openness, conscientiousness, extraversion,
    /// agreeableness, neuroticism (0–255), stable key. Folk, culture and epithets are in <see cref="HistoryPersonText"/>.
    /// </summary>
    [JSExport]
    public static int[] HistoryPersons()
    {
        var persons = Current.History.Persons;
        var result = new int[persons.Count * PersonStride];
        for (int i = 0; i < persons.Count; i++)
        {
            var p = persons[i];
            int o = i * PersonStride;
            result[o] = p.Id; result[o + 1] = (int)p.Kind; result[o + 2] = p.PolityId; result[o + 3] = p.SeatSettlement;
            result[o + 4] = p.Feminine ? 1 : 0; result[o + 5] = p.HouseId; result[o + 6] = p.Parent; result[o + 7] = p.BornTurn;
            result[o + 8] = p.BornYear; result[o + 9] = p.DiedTurn; result[o + 10] = p.DeathCause; result[o + 11] = p.Renown;
            result[o + 12] = Scaled(p.Wealth, 100); result[o + 13] = p.Loyalty; result[o + 14] = Scaled(p.Pledged, 100);
            result[o + 15] = p.Martial; result[o + 16] = p.Intrigue; result[o + 17] = p.Stewardship; result[o + 18] = p.Charisma;
            result[o + 19] = p.Traits.Openness; result[o + 20] = p.Traits.Conscientiousness; result[o + 21] = p.Traits.Extraversion;
            result[o + 22] = p.Traits.Agreeableness; result[o + 23] = p.Traits.Neuroticism; result[o + 24] = p.StableKey;
        }
        return result;
    }

    private static int Scaled(double v, double scale)
        => double.IsNaN(v) ? 0 : (int)Math.Round(Math.Max(int.MinValue / scale, Math.Min(int.MaxValue / scale, v)) * scale);

    /// <summary>Per person, one string: <c>folk\tculture\tepithet,epithet</c>.</summary>
    [JSExport]
    public static string[] HistoryPersonText()
    {
        var h = Current.History;
        return h.Persons.Select(p => $"{p.FolkKey}\t{p.CultureKey}\t{string.Join(",", p.Epithets.Select(h.Strings.NameOf))}").ToArray();
    }

    /// <summary>Per bond (<see cref="RelationStride"/> ints): person A, person B, kind (string id), from turn, to turn (-1 open), weight.</summary>
    [JSExport]
    public static int[] HistoryRelations()
    {
        var relations = Current.History.Relations;
        var result = new int[relations.Count * RelationStride];
        for (int i = 0; i < relations.Count; i++)
        {
            var r = relations[i];
            int o = i * RelationStride;
            result[o] = r.A; result[o + 1] = r.B; result[o + 2] = r.Kind; result[o + 3] = r.FromTurn; result[o + 4] = r.ToTurn; result[o + 5] = r.Weight;
        }
        return result;
    }

    /// <summary>Per tenure (<see cref="TenureStride"/> ints): person, polity, office (string id), from turn, to turn (-1 open).</summary>
    [JSExport]
    public static int[] HistoryTenures()
    {
        var tenures = Current.History.Tenures;
        var result = new int[tenures.Count * TenureStride];
        for (int i = 0; i < tenures.Count; i++)
        {
            var t = tenures[i];
            int o = i * TenureStride;
            result[o] = t.Person; result[o + 1] = t.Polity; result[o + 2] = t.Office; result[o + 3] = t.FromTurn; result[o + 4] = t.ToTurn;
        }
        return result;
    }

    /// <summary>
    /// Per battle (<see cref="BattleStride"/> ints): id, turn, year, season, kind (<see cref="HistoryBattleKindNames"/>), tile,
    /// settlement (-1 unless a siege), outcome (<see cref="HistoryBattleOutcomeNames"/>), parent battle (-1), narrating event (-1),
    /// siege months, siege end (string id or -1); then per side (attacker, defender): polity, general (-1), strength, loss ×1000,
    /// supply ×1000, morale ×1000. Terrain, tactics, units and beats are in <see cref="HistoryBattleText"/>.
    /// </summary>
    [JSExport]
    public static int[] HistoryBattles()
    {
        var battles = Current.History.Battles;
        var result = new int[battles.Count * BattleStride];
        for (int i = 0; i < battles.Count; i++)
        {
            var b = battles[i];
            int o = i * BattleStride;
            result[o] = b.Id; result[o + 1] = b.Turn; result[o + 2] = b.Year; result[o + 3] = (int)b.Season; result[o + 4] = (int)b.Kind;
            result[o + 5] = b.Tile; result[o + 6] = b.Settlement; result[o + 7] = (int)b.Outcome; result[o + 8] = b.Parent;
            result[o + 9] = b.EventId; result[o + 10] = b.SiegeMonths; result[o + 11] = b.SiegeEnd;
            Side(result, o + 12, b.Attacker);
            Side(result, o + 18, b.Defender);
        }
        return result;

        static void Side(int[] r, int o, BattleSide s)
        {
            r[o] = s.Polity; r[o + 1] = s.General; r[o + 2] = Scaled(s.Strength, 1); r[o + 3] = Scaled(s.LossShare, 1000);
            r[o + 4] = Scaled(s.Supply, 1000); r[o + 5] = Scaled(s.Morale, 1000);
        }
    }

    /// <summary>Per battle, one string: <c>terrain\tattackerTactic\tattackerUnit\tdefenderTactic\tdefenderUnit\tbeat,beat</c> (empty for none).</summary>
    [JSExport]
    public static string[] HistoryBattleText()
    {
        var h = Current.History;
        string Name(int id) => id >= 0 && id < h.Strings.Count ? h.Strings.NameOf(id) : string.Empty;
        return h.Battles.Select(b => string.Join("\t", Name(b.Terrain), Name(b.Attacker.Tactic), Name(b.Attacker.Unit), Name(b.Defender.Tactic),
            Name(b.Defender.Unit), string.Join(",", b.Beats.Select(Name)))).ToArray();
    }

    /// <summary>
    /// Per route state (<see cref="RouteStateStride"/> ints): route A, route B (capital tiles), safety ×1000, traffic ×1000, forts,
    /// waystations, outlaw band (person or -1), sponsor (polity or -1), sabotaged turn (-1), last incident turn (-1).
    /// </summary>
    [JSExport]
    public static int[] HistoryRouteStates()
    {
        var states = Current.History.RouteStates;
        var result = new int[states.Count * RouteStateStride];
        for (int i = 0; i < states.Count; i++)
        {
            var s = states[i];
            int o = i * RouteStateStride;
            result[o] = s.RouteA; result[o + 1] = s.RouteB; result[o + 2] = Scaled(s.Safety, 1000); result[o + 3] = Scaled(s.Traffic, 1000);
            result[o + 4] = s.Forts; result[o + 5] = s.Waystations; result[o + 6] = s.OutlawBand; result[o + 7] = s.Sponsor;
            result[o + 8] = s.SabotagedTurn; result[o + 9] = s.LastIncidentTurn;
        }
        return result;
    }

    /// <summary>Every route sample (<see cref="RouteSampleStride"/> ints): turn, route A, route B, safety ×1000, traffic ×1000; by turn, then route.</summary>
    [JSExport]
    public static int[] HistoryRouteSeries()
    {
        var samples = Current.History.RouteSamples;
        var result = new int[samples.Count * RouteSampleStride];
        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            int o = i * RouteSampleStride;
            result[o] = s.Turn; result[o + 1] = s.RouteA; result[o + 2] = s.RouteB; result[o + 3] = Scaled(s.Safety, 1000); result[o + 4] = Scaled(s.Traffic, 1000);
        }
        return result;
    }
}
