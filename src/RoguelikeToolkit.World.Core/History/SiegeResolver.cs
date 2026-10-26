using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Resolves one siege month by month (docs/HISTORY_EXPANSION_PLAN.md §3.1), up to the history's <c>MaxSiegeMonths</c>. A pure
/// function of the compiled data (<see cref="WarfareProfile.Siege"/>), the two sides, the place and named draw streams: the
/// month loop draws a fixed four values a month from <c>siege:months</c> whatever happens, and a relief battle draws from its
/// own <c>relief:</c> streams through <see cref="BattleResolver"/>. <see cref="DetMath"/> only. Each month:
/// <list type="number">
/// <item>The besiegers' supply falls (faster in winter) and disease thins them (faster in a marsh camp or in summer); below
/// <see cref="Peoples.SiegeDef.LiftBelow"/> they lift the siege.</item>
/// <item>A relief army, if the defender has one, arrives in the month its march takes and fights the besiegers; if it wins the
/// siege is relieved, if not the defenders lose heart.</item>
/// <item>When the besiegers' power against the walls (<c>SiegeSkill/100 × engines × Pa / (Pd(1 + walls))</c>) passes
/// <see cref="Peoples.SiegeDef.StormRatio"/> they may storm: success is <c>r²/(1+r²)</c>, failure costs them.</item>
/// <item>Past its food the place starves: its morale falls and each month it may surrender.</item>
/// <item>Someone may open a gate: more likely with a cunning enemy spymaster or a starving town.</item>
/// </list>
/// A siege still standing at the limit is abandoned.
/// </summary>
internal static class SiegeResolver
{
    /// <summary>Siege end: the walls were stormed.</summary>
    public const string Stormed = "stormed";
    /// <summary>Siege end: the place surrendered.</summary>
    public const string Surrendered = "surrendered";
    /// <summary>Siege end: someone opened a gate.</summary>
    public const string Betrayed = "betrayed";
    /// <summary>Siege end: a relief army beat the besiegers.</summary>
    public const string Relieved = "relieved";
    /// <summary>Siege end: the besiegers' supply gave out.</summary>
    public const string Lifted = "lifted";
    /// <summary>Siege end: the besiegers gave up at the limit.</summary>
    public const string Abandoned = "abandoned";

    /// <summary>True when <paramref name="end"/> is a fall (stormed, surrendered or betrayed).</summary>
    public static bool Fell(string end) => end is Stormed or Surrendered or Betrayed;

    /// <summary>
    /// The besieged place: its garrison (fighting strength), walls, whether it is a city, whether a river or the sea still
    /// feeds it, whether the besiegers camp in a marsh, its terrain index (for a relief battle) and the season the siege began.
    /// </summary>
    internal readonly record struct Place(int Tile, double Garrison, double Walls, bool City, bool Resupply, bool Marsh, int Terrain,
        HistorySeason Season, double Forage = 0.5, double SeasonalRange = 0);

    /// <summary>A relief army: its levy and how far it marches (degrees). None when the levy is zero.</summary>
    internal readonly record struct Relief(double Levy, double Distance);

    /// <summary>The siege.</summary>
    internal sealed record Result(int Months, string End, BattleOutcome Outcome, HistorySeason Season, double AttackerStrength, double AttackerLoss,
        double DefenderLoss, double AttackerSupply, double DefenderMorale, int Tactic, double Casualty, int ReliefMonth, BattleResolver.Result? ReliefBattle,
        IReadOnlyList<string> Beats);

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    /// <summary>Months of food the place holds.</summary>
    public static double FoodMonths(WarfareProfile profile, Place place)
    {
        var s = profile.Siege;
        return s.FoodBase + (place.City ? s.FoodCity : 0) + (place.Resupply ? s.FoodResupply : 0) + profile.FoodSeason[(int)place.Season];
    }

    /// <summary>
    /// Runs the siege of <paramref name="place"/> by <paramref name="attacker"/> (its host: <paramref name="strength"/> at
    /// <paramref name="supply"/> and <paramref name="morale"/>) against <paramref name="defender"/>, with an optional relief army.
    /// </summary>
    public static Result Resolve(WarfareProfile profile, BattleResolver.Side attacker, double strength, double supply, double morale,
        BattleResolver.Side defender, Place place, Relief relief, Func<string, Rng> draws, int maxMonths, int turnYears)
    {
        var s = profile.Siege;
        maxMonths = Math.Max(1, maxMonths);
        double food = FoodMonths(profile, place);
        double start = Math.Max(0, strength), host = start, garrison0 = Math.Max(1e-9, place.Garrison), garrison = garrison0;
        double sigma = Clamp(supply, 0, 1), moraleA = Math.Max(0.05, morale), moraleD = s.DefenderMorale;
        var engagement = draws("siege:engines");
        int tactic = BattleResolver.DrawTactic(profile, attacker, initiative: true, place.Terrain, (int)place.Season, engagement.NextDouble(), BattleKind.Siege);
        double engines = tactic >= 0 ? s.EnginePower : 1;
        double skill = attacker.Way.SiegeSkill / 100.0;
        bool spy = attacker.SpyIntrigue > s.BetrayalIntrigue;
        int reliefMonth = relief.Levy > 0 && relief.Levy >= s.ReliefShare * start
            ? Math.Max(1, (int)Math.Ceiling(relief.Distance / s.ReliefDegreesPerMonth)) : -1;
        BattleResolver.Result? reliefBattle = null;
        var months = draws("siege:months");
        string end = Abandoned;
        int month = 0, season = (int)place.Season;
        while (month < maxMonths)
        {
            month++;
            season = ((int)place.Season + (month - 1) / 3) % WarfareProfile.Seasons;
            double tryRoll = months.NextDouble(), stormRoll = months.NextDouble(), surrenderRoll = months.NextDouble(), betrayRoll = months.NextDouble();
            bool winter = season == (int)HistorySeason.Winter, summer = season == (int)HistorySeason.Summer;
            sigma -= s.SupplyDecay * (winter ? s.WinterDecay : 1);
            host *= 1 - Clamp(s.Disease * (place.Marsh || summer ? s.DiseaseRife : 1), 0, 1);
            if (sigma < s.LiftBelow) { end = Lifted; break; }

            if (month == reliefMonth)
            {
                // The relief army attacks the besiegers on the ground before the walls.
                var reliever = defender with { Levy = relief.Levy };
                var besiegers = attacker with { Levy = host };
                var field = new BattleResolver.Field(place.Tile, place.Terrain, false, place.Forage, place.SeasonalRange, relief.Distance);
                reliefBattle = BattleResolver.Resolve(profile, reliever, besiegers, new[] { field }, p => draws("relief:" + p), turnYears, BattleKind.Relief);
                host *= 1 - reliefBattle.Defender.Loss;
                // A won field, or besiegers who would not meet it (they withdraw: an attacker victory), relieve the place.
                if (reliefBattle.AttackerWon)
                {
                    end = Relieved;
                    break;
                }
                moraleD = Math.Max(0.05, moraleD - s.ReliefFailedMorale);
            }

            double pa = host * sigma * moraleA, pd = garrison * moraleD;
            double r = skill * engines * pa / Math.Max(1e-9, pd * (1 + place.Walls));
            if (r > s.StormRatio && tryRoll < s.StormChance * skill)
            {
                if (stormRoll < r * r / (1 + r * r)) { end = Stormed; break; }
                host *= 1 - s.StormLoss;
                garrison *= 1 - s.StormLoss / 2;
            }
            bool starving = month > food;
            if (starving)
            {
                moraleD = Math.Max(0.05, moraleD - s.StarveMorale);
                if (surrenderRoll < s.SurrenderPerMonth * (month - food)) { end = Surrendered; break; }
            }
            if (betrayRoll < s.BetrayalBase + (spy ? s.BetrayalSpy : 0) + (starving ? s.BetrayalStarving : 0)) { end = Betrayed; break; }
        }

        var outcome = end switch
        {
            Stormed => BattleOutcome.AttackerDecisive,
            Surrendered or Betrayed => BattleOutcome.AttackerVictory,
            Relieved or Lifted => BattleOutcome.DefenderVictory,
            _ => BattleOutcome.Stalemate,
        };
        var fate = draws("siege:fall");
        double casualty = end == Stormed ? s.StormedCasualtyMin + (s.StormedCasualtyMax - s.StormedCasualtyMin) * fate.NextDouble() : 0;
        bool fell = Fell(end);
        var facts = new BattleResolver.Facts(season, place.Terrain, BattleKind.Siege, false, false, 0, fell ? 0 : outcome == BattleOutcome.Stalemate ? 2 : 1,
            BattleResolver.OutcomeClass(outcome), fell ? tactic : -1, fell ? -1 : tactic, fell ? attacker : outcome == BattleOutcome.Stalemate ? null : defender,
            false, false, SiegeEnd: end);
        var beats = BattleResolver.Beats(profile, facts, fate.NextDouble());
        return new Result(month, end, outcome, (HistorySeason)season, host, start > 0 ? Clamp(1 - host / start, 0, 1) : 0,
            Clamp(1 - garrison / garrison0, 0, 1), Math.Max(0, sigma), moraleD, tactic, casualty, reliefMonth, reliefBattle, beats);
    }
}
