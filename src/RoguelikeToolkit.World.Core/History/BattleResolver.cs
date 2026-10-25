using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Resolves one field battle (docs/HISTORY_EXPANSION_PLAN.md §3). A pure function of the compiled data
/// (<see cref="WarfareProfile"/>), the two sides, the candidate fields and four named draw streams
/// (<c>battle:season</c>, <c>battle:manoeuvre</c>, <c>battle:engagement</c>, <c>battle:rout</c>); every stream draws a fixed
/// number of values whatever happens, so one phase's choices never shift another's draws. <see cref="DetMath"/> only.
/// <list type="number">
/// <item>Season and year from the attacker's campaign weights.</item>
/// <item>Manoeuvre: initiative (mobility, the commander's martial and intrigue, noise). The winner picks the field among the
/// candidates by how well its doctrine fits the ground (the defender also likes cover, the attacker dislikes it); an attacker
/// with the initiative may ford a river elsewhere. Each side musters the share of its levy within reach of the field; the
/// attacker's host also thins with its supply line; an unwarned defender musters part of what it could.
/// An attacker whose supply falls below <see cref="Peoples.BattleDef.AvertedBelow"/> never reaches the field.</item>
/// <item>Engagement: each side draws a tactic (doctrine × terrain fit × season × the commander's epithets; initiative tactics
/// only for the manoeuvre winner); power multiplies strength, morale, supply (softened by <see cref="Peoples.BattleDef.SupplyPower"/>),
/// fit, counters, martial skill and winter; the
/// margin is <c>MarginScale·log(Pa/Pd)</c> plus logistic noise, so equal noise gives the attacker <c>r²/(1+r²)</c> at
/// MarginScale 2.</item>
/// <item>Refusal: a side that won the manoeuvre declines a field where its power is below
/// <see cref="Peoples.BattleDef.DeclineBelow"/> of the enemy's (a defender lets the raid through, an attacker turns back).</item>
/// <item>Rout: losses by margin, pursuit after a rout, winter attrition; commanders may fall or be taken for ransom; one
/// turning-point beat by weight plus the beats that always follow their conditions.</item>
/// </list>
/// </summary>
internal static class BattleResolver
{
    /// <summary>A commander as the battle sees them.</summary>
    internal sealed record Commander(int Person, double Martial, double Intrigue, int Renown, IReadOnlyList<string> Epithets);

    /// <summary>One side before the battle. <see cref="Levy"/> is the full levy, before reach or readiness.</summary>
    internal sealed record Side(int Polity, double Levy, WarfareProfile.Way Way, Commander? General = null, double SpyIntrigue = 50,
        double Honor = 50, double Pressure = 0);

    /// <summary>
    /// A candidate field: terrain index, whether the attacker must cross a river, what the land offers a forager (0..1), the
    /// seasonal temperature range, the attacker's distance in degrees from its nearest settlement (its supply line), the polity
    /// whose holy ground it is (or -1), and the share of each side's levy that musters there (1 = all of it).
    /// </summary>
    internal readonly record struct Field(int Tile, int Terrain, bool River, double Forage, double SeasonalRange, double Distance, int HolyOwner = -1,
        double AttackerMuster = 1, double DefenderMuster = 1);

    /// <summary>One side after the battle. <see cref="Tactic"/> is a tactic index or -1.</summary>
    internal sealed record SideResult(double Strength, double Loss, double Supply, double Morale, int Tactic, string Unit, bool GeneralFell,
        bool GeneralCaptured);

    /// <summary>The battle.</summary>
    internal sealed record Result(int Field, HistorySeason Season, int Year, BattleKind Kind, int Terrain, bool River, bool RiverBypassed,
        bool AttackerInitiative, bool Warned, BattleOutcome Outcome, double Margin, SideResult Attacker, SideResult Defender,
        IReadOnlyList<string> Beats, bool Declined = false)
    {
        /// <summary>True when the attacker won (decisive, victory or pyrrhic).</summary>
        public bool AttackerWon => Outcome is BattleOutcome.AttackerDecisive or BattleOutcome.AttackerVictory or BattleOutcome.AttackerPyrrhic;

        /// <summary>True when the defender won.</summary>
        public bool DefenderWon => Outcome is BattleOutcome.DefenderVictory or BattleOutcome.DefenderDecisive;
    }

    /// <summary>Class of an outcome for the <c>outcome=</c> beat condition (index into <see cref="WarfareProfile.OutcomeClasses"/>).</summary>
    internal static int OutcomeClass(BattleOutcome o) => o switch
    {
        BattleOutcome.AttackerDecisive or BattleOutcome.DefenderDecisive => 0,
        BattleOutcome.AttackerVictory or BattleOutcome.DefenderVictory => 1,
        BattleOutcome.AttackerPyrrhic => 2,
        BattleOutcome.Stalemate => 3,
        _ => 4,
    };

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    private static double Tanh(double x)
    {
        double e = DetMath.Exp(2 * Clamp(x, -20, 20));
        return (e - 1) / (e + 1);
    }

    /// <summary>Resolves the battle between <paramref name="attacker"/> and <paramref name="defender"/> over <paramref name="fields"/> (at least one).</summary>
    public static Result Resolve(WarfareProfile profile, Side attacker, Side defender, IReadOnlyList<Field> fields, Func<string, Rng> draws, int turnYears)
    {
        if (fields == null || fields.Count == 0) throw new ArgumentException("A battle needs at least one candidate field.", nameof(fields));
        var data = profile.Data;

        // 1. Season and year.
        var seasonRng = draws("battle:season");
        int season = Weighted(ref seasonRng, attacker.Way.CampaignSeasons);
        if (season < 0) season = (int)HistorySeason.Summer;
        int year = (int)seasonRng.NextUInt((uint)Math.Max(1, turnYears));

        // 2. Manoeuvre: initiative, the field, the river, reach, warning, supply.
        var move = draws("battle:manoeuvre");
        double ia = Initiative(profile, attacker, ref move), id = Initiative(profile, defender, ref move);
        bool attackerInitiative = ia > id;
        double pick = move.NextDouble(), bypassRoll = move.NextDouble(), warnRoll = move.NextDouble();
        int fieldIndex = PickField(profile, attackerInitiative ? attacker : defender, !attackerInitiative, fields, season, pick);
        var field = fields[fieldIndex];
        bool bypassed = field.River && attackerInitiative && bypassRoll < data.RiverBypass * attacker.Way.Mobility / 100.0;
        bool river = field.River && !bypassed;
        double reach = DetMath.Exp(-Math.Max(0, field.Distance) / (data.ReachDegrees * (0.5 + attacker.Way.Mobility / 100.0)));
        bool warned = warnRoll < data.WarnedBase + data.WarnedPerIntrigue * (defender.SpyIntrigue - attacker.SpyIntrigue);
        double fa = Math.Max(0, attacker.Levy) * Muster(data, field.AttackerMuster) * reach;
        double fd = Math.Max(0, defender.Levy) * Muster(data, field.DefenderMuster) * (warned ? 1 : data.DefenderReadiness);
        double sa = Clamp(reach * profile.SeasonSupply[season] + attacker.Way.Forage / 100.0 * Clamp(field.Forage, 0, 1) * (1 - reach), data.SupplyMin, 1);
        const double sd = 1;
        double winter = season == (int)HistorySeason.Winter ? data.WinterPenalty * Clamp(field.SeasonalRange / data.WinterRange, 0, 1) : 0;
        double wa = Clamp(winter * (1 - attacker.Way.ColdTolerance / 100.0), 0, 1);
        double wd = Clamp(winter * (1 - defender.Way.ColdTolerance / 100.0) * data.WinterDefenderShare, 0, 1);
        double ma = Morale(data, attacker, home: false, holy: field.HolyOwner == attacker.Polity, sa);
        double md = Morale(data, defender, home: true, holy: field.HolyOwner == defender.Polity, sd);

        // 3. Engagement.
        var engage = draws("battle:engagement");
        double tacticA = engage.NextDouble(), tacticD = engage.NextDouble(), unitA = engage.NextDouble(), unitD = engage.NextDouble(), noise = engage.NextDouble();
        var rout = draws("battle:rout");
        double fateA = rout.NextDouble(), ransomA = rout.NextDouble(), fateD = rout.NextDouble(), ransomD = rout.NextDouble(), beatRoll = rout.NextDouble();

        if (sa < data.AvertedBelow)
        {
            double attrition = Clamp(data.WinterAttrition * wa + (1 - sa) * data.LoserLossBase, 0, 0.95);
            var none = new SideResult(fd, 0, sd, md, -1, string.Empty, false, false);
            return new Result(fieldIndex, (HistorySeason)season, year, BattleKind.Field, field.Terrain, river, bypassed, attackerInitiative, warned,
                BattleOutcome.Averted, 0, new SideResult(fa, attrition, sa, ma, -1, string.Empty, false, false), none, Array.Empty<string>());
        }

        int ta = DrawTactic(profile, attacker, attackerInitiative, field.Terrain, season, tacticA);
        int td = DrawTactic(profile, defender, !attackerInitiative, field.Terrain, season, tacticD);
        var kind = (ta >= 0 && profile.Tactics[ta].Initiative) || (td >= 0 && profile.Tactics[td].Initiative) ? BattleKind.Ambush : BattleKind.Field;
        double delta = profile.Defence[field.Terrain] + (river ? data.RiverDefence : 0);
        if (ta >= 0) delta *= profile.Tactics[ta].Cover;
        double pa = fa * ma * (1 - data.SupplyPower * (1 - sa)) * Fit(profile, ta, td, field.Terrain, season) * (1 + data.MartialPower * (MartialOf(attacker) - 50)) * (1 - wa);
        double pd = fd * md * (1 - data.SupplyPower * (1 - sd)) * Fit(profile, td, ta, field.Terrain, season) * (1 + data.MartialPower * (MartialOf(defender) - 50)) * (1 - wd) * (1 + delta);
        double spread = Math.Max(data.MarginSpreadMin, data.MarginSpread * (1 + Spread(profile, attacker) + Spread(profile, defender)));
        double u = Clamp(noise, 1e-12, 1 - 1e-12);
        double margin = data.MarginScale * DetMath.Log(Math.Max(pa, 1e-12) / Math.Max(pd, 1e-12)) + spread * DetMath.Log(u / (1 - u));
        var outcome = Band(data, margin);

        // A side that won the manoeuvre does not offer a battle it expects to lose badly: a defender lets the raid through, an
        // attacker turns back. Only a side caught without the initiative can be routed from a hopeless field.
        bool attackerDeclines = attackerInitiative && pa < data.DeclineBelow * pd;
        bool defenderDeclines = !attackerInitiative && pd < data.DeclineBelow * pa;
        if (attackerDeclines || defenderDeclines)
        {
            var declined = new Facts(season, field.Terrain, BattleKind.Field, river, bypassed, attackerInitiative ? 0 : 1, defenderDeclines ? 0 : 2,
                OutcomeClass(defenderDeclines ? BattleOutcome.AttackerVictory : BattleOutcome.Averted), defenderDeclines ? ta : -1, defenderDeclines ? td : -1,
                defenderDeclines ? attacker : null, false, false, Declined: true);
            double lw = Clamp(data.WinnerLossMin + data.WinterAttrition * wa, 0, 0.95);
            return new Result(fieldIndex, (HistorySeason)season, year, BattleKind.Field, field.Terrain, river, bypassed, attackerInitiative, warned,
                defenderDeclines ? BattleOutcome.AttackerVictory : BattleOutcome.Averted, defenderDeclines ? data.VictoryMargin : 0,
                new SideResult(fa, lw, sa, ma, ta, Unit(profile, attacker, ta, unitA), false, false),
                new SideResult(fd, Clamp((defenderDeclines ? data.LoserLossMin : 0) + data.WinterAttrition * wd, 0, 0.95), sd, md, td,
                    Unit(profile, defender, td, unitD), false, false),
                Beats(profile, declined, beatRoll), Declined: true);
        }

        // 4. Rout: losses, pursuit, winter, commanders, beats.
        bool attackerWon = outcome is BattleOutcome.AttackerDecisive or BattleOutcome.AttackerVictory or BattleOutcome.AttackerPyrrhic;
        bool defenderWon = outcome is BattleOutcome.DefenderVictory or BattleOutcome.DefenderDecisive;
        bool decisive = outcome is BattleOutcome.AttackerDecisive or BattleOutcome.DefenderDecisive;
        double am = Math.Abs(margin);
        double winnerLoss = Clamp(data.WinnerLossBase / (1 + am), data.WinnerLossMin, data.WinnerLossMax);
        double loserLoss = Clamp(data.LoserLossBase + data.LoserLossPerMargin * am, data.LoserLossMin, data.LoserLossMax);
        double la = attackerWon || !defenderWon ? winnerLoss : loserLoss;
        double ld = defenderWon || !attackerWon ? winnerLoss : loserLoss;
        if (decisive)
        {
            var (winner, loser) = attackerWon ? (attacker, defender) : (defender, attacker);
            double pursuit = data.PursuitLoss * winner.Way.Mobility / 100.0 * (1 - loser.Way.Discipline / data.PursuitDiscipline);
            foreach (var c in winner.Way.Customs) pursuit *= c.Pursuit;
            if (attackerWon) ld += Math.Max(0, pursuit);
            else la += Math.Max(0, pursuit);
        }
        la = Clamp(la + data.WinterAttrition * wa, 0, 0.95);
        ld = Clamp(ld + data.WinterAttrition * wd, 0, 0.95);

        var (fellA, takenA) = Fate(profile, attacker, defender, defenderWon, outcome == BattleOutcome.DefenderDecisive, fateA, ransomA);
        var (fellD, takenD) = Fate(profile, defender, attacker, attackerWon, outcome == BattleOutcome.AttackerDecisive, fateD, ransomD);

        var facts = new Facts(season, field.Terrain, kind, river, bypassed, attackerInitiative ? 0 : 1,
            attackerWon ? 0 : defenderWon ? 1 : 2, OutcomeClass(outcome),
            attackerWon ? ta : defenderWon ? td : -1, attackerWon ? td : defenderWon ? ta : -1,
            attackerWon ? attacker : defenderWon ? defender : null, fellA || fellD, takenA || takenD);
        var beats = Beats(profile, facts, beatRoll);

        return new Result(fieldIndex, (HistorySeason)season, year, kind, field.Terrain, river, bypassed, attackerInitiative, warned, outcome, margin,
            new SideResult(fa, la, sa, ma, ta, Unit(profile, attacker, ta, unitA), fellA, takenA),
            new SideResult(fd, ld, sd, md, td, Unit(profile, defender, td, unitD), fellD, takenD),
            beats);
    }

    private static double Muster(Peoples.BattleDef data, double local) => data.MusterFloor + (1 - data.MusterFloor) * Clamp(local, 0, 1);

    private static double MartialOf(Side side) => side.General?.Martial ?? 50;

    private static double Initiative(WarfareProfile profile, Side side, ref Rng rng)
    {
        var data = profile.Data;
        double i = data.InitiativeMobility * side.Way.Mobility
            + data.InitiativeMartial * (side.General?.Martial ?? 50) + data.InitiativeIntrigue * (side.General?.Intrigue ?? 50);
        if (side.General != null)
            foreach (var e in side.General.Epithets)
                if (profile.EpithetOf(e) is { } effect) i += effect.Initiative;
        return i + data.InitiativeNoise * (rng.NextDouble() + rng.NextDouble() - 1);
    }

    private static double Morale(Peoples.BattleDef data, Side side, bool home, bool holy, double supply)
    {
        double m = data.MoraleBase + data.MoraleHonor * side.Honor + (home ? data.MoraleHome : 0) + (holy ? data.MoraleHoly : 0)
            + data.MoraleRenown * Tanh((side.General?.Renown ?? 0) / data.RenownScale)
            - data.MoralePressure * Math.Max(0, side.Pressure - data.PressureFrom) - data.MoraleHunger * (1 - supply);
        foreach (var c in side.Way.Customs) m += c.Morale;
        return Clamp(m, data.MoraleMin, data.MoraleMax);
    }

    private static double Spread(WarfareProfile profile, Side side)
    {
        double s = 0;
        if (side.General != null)
            foreach (var e in side.General.Epithets)
                if (profile.EpithetOf(e) is { } effect) s += effect.Spread;
        return s;
    }

    /// <summary>Weight of tactic <paramref name="t"/> for <paramref name="side"/> on this ground in this season.</summary>
    private static double TacticWeight(WarfareProfile profile, Side side, int t, int terrain, int season)
    {
        var tactic = profile.Tactics[t];
        double w = side.Way.Doctrine[t] * tactic.Fit[terrain] * tactic.Season[season];
        if (side.General != null)
            foreach (var e in side.General.Epithets)
                if (profile.EpithetOf(e) is { } effect) w *= effect.Tactics[t];
        return w;
    }

    private static int DrawTactic(WarfareProfile profile, Side side, bool initiative, int terrain, int season, double roll)
    {
        var tactics = profile.Tactics;
        double total = 0;
        for (int t = 0; t < tactics.Length; t++)
            if (Usable(tactics[t], initiative)) total += TacticWeight(profile, side, t, terrain, season);
        if (!(total > 0)) return -1;
        double target = roll * total;
        int last = -1;
        for (int t = 0; t < tactics.Length; t++)
        {
            if (!Usable(tactics[t], initiative)) continue;
            double w = TacticWeight(profile, side, t, terrain, season);
            if (!(w > 0)) continue;
            last = t;
            target -= w;
            if (target < 0) return t;
        }
        return last;
    }

    private static bool Usable(WarfareProfile.Tactic tactic, bool initiative)
        => tactic.Kinds[(int)BattleKind.Field] && (initiative || !tactic.Initiative);

    /// <summary>Terrain and season fit, counter against the enemy's tactic, and surprise.</summary>
    private static double Fit(WarfareProfile profile, int mine, int theirs, int terrain, int season)
    {
        if (mine < 0) return 1;
        var t = profile.Tactics[mine];
        return t.Fit[terrain] * t.Season[season] * (theirs >= 0 ? t.Counter[theirs] : 1) * t.Surprise;
    }

    /// <summary>The manoeuvre winner's choice of field: expected doctrine fit, times cover for a defender (over cover for an attacker), sharpened.</summary>
    private static int PickField(WarfareProfile profile, Side chooser, bool chooserDefends, IReadOnlyList<Field> fields, int season, double roll)
    {
        if (fields.Count == 1) return 0;
        var data = profile.Data;
        var scores = new double[fields.Count];
        double total = 0;
        double doctrine = 0;
        for (int t = 0; t < profile.Tactics.Length; t++)
            if (Usable(profile.Tactics[t], initiative: true)) doctrine += chooser.Way.Doctrine[t];
        for (int f = 0; f < fields.Count; f++)
        {
            int terrain = fields[f].Terrain;
            double fit = 0;
            for (int t = 0; t < profile.Tactics.Length; t++)
                if (Usable(profile.Tactics[t], initiative: true)) fit += chooser.Way.Doctrine[t] * profile.Tactics[t].Fit[terrain] * profile.Tactics[t].Season[season];
            fit = doctrine > 0 ? fit / doctrine : 1;
            double cover = 1 + profile.Defence[terrain] + (fields[f].River ? data.RiverDefence : 0);
            double score = fit * (chooserDefends ? cover : 1 / cover);
            scores[f] = DetMath.Pow(Math.Max(score, 1e-9), data.FieldSharpness);
            total += scores[f];
        }
        double target = roll * total;
        for (int f = 0; f < scores.Length; f++)
        {
            target -= scores[f];
            if (target < 0) return f;
        }
        return scores.Length - 1;
    }

    private static BattleOutcome Band(Peoples.BattleDef data, double m)
    {
        if (m > data.DecisiveMargin) return BattleOutcome.AttackerDecisive;
        if (m > data.VictoryMargin) return BattleOutcome.AttackerVictory;
        if (m > data.StalemateMargin) return BattleOutcome.AttackerPyrrhic;
        if (m >= -data.StalemateMargin) return BattleOutcome.Stalemate;
        if (m >= -data.DecisiveMargin) return BattleOutcome.DefenderVictory;
        return BattleOutcome.DefenderDecisive;
    }

    /// <summary>Whether <paramref name="side"/>'s commander falls, and whether the enemy takes them for ransom instead.</summary>
    private static (bool Fell, bool Taken) Fate(WarfareProfile profile, Side side, Side enemy, bool lost, bool routed, double fateRoll, double ransomRoll)
    {
        if (side.General == null) return (false, false);
        var data = profile.Data;
        double p = data.GeneralDeathBase + (lost ? data.GeneralDeathLost : 0) + (routed ? data.GeneralDeathRouted : 0);
        foreach (var e in side.General.Epithets)
            if (profile.EpithetOf(e) is { } effect) p += effect.Death;
        if (!(fateRoll < Clamp(p, 0, 1))) return (false, false);
        double capture = 0;
        foreach (var c in enemy.Way.Customs) capture = Math.Max(capture, c.Capture);
        return ransomRoll < capture ? (false, true) : (true, false);
    }

    private static string Unit(WarfareProfile profile, Side side, int tactic, double roll)
    {
        if (tactic < 0) return string.Empty;
        string arm = profile.Tactics[tactic].Arm;
        double total = 0;
        foreach (var u in side.Way.Units)
            if (string.Equals(u.Arm, arm, StringComparison.OrdinalIgnoreCase)) total += u.Weight;
        if (!(total > 0)) return string.Empty;
        double target = roll * total;
        string last = string.Empty;
        foreach (var u in side.Way.Units)
        {
            if (!string.Equals(u.Arm, arm, StringComparison.OrdinalIgnoreCase)) continue;
            last = u.Key;
            target -= u.Weight;
            if (target < 0) return u.Key;
        }
        return last;
    }

    private static int Weighted(ref Rng rng, double[] weights)
    {
        double total = 0;
        foreach (double w in weights) total += Math.Max(0, w);
        double target = rng.NextDouble() * total;
        if (!(total > 0)) return -1;
        for (int i = 0; i < weights.Length; i++)
        {
            target -= Math.Max(0, weights[i]);
            if (target < 0) return i;
        }
        return weights.Length - 1;
    }

    /// <summary>What the beat conditions test.</summary>
    private sealed record Facts(int Season, int Terrain, BattleKind Kind, bool River, bool RiverBypassed, int Initiative, int Winner,
        int OutcomeClass, int WinnerTactic, int LoserTactic, Side? WinnerSide, bool GeneralFell, bool GeneralCaptured, bool Declined = false);

    private static bool Holds(WarfareProfile profile, WarfareProfile.Condition c, Facts f)
    {
        string? Arm(int tactic) => tactic >= 0 ? profile.Tactics[tactic].Arm : null;
        bool v = c.Test switch
        {
            WarfareProfile.Test.Season => f.Season == c.Value,
            WarfareProfile.Test.Terrain => f.Terrain == c.Value,
            WarfareProfile.Test.Kind => (int)f.Kind == c.Value,
            WarfareProfile.Test.River => f.River,
            WarfareProfile.Test.RiverBypassed => f.RiverBypassed,
            WarfareProfile.Test.Initiative => f.Initiative == c.Value,
            WarfareProfile.Test.Winner => f.Winner == c.Value,
            WarfareProfile.Test.Outcome => f.OutcomeClass == c.Value,
            WarfareProfile.Test.WinnerTactic => f.WinnerTactic == c.Value,
            WarfareProfile.Test.LoserTactic => f.LoserTactic == c.Value,
            WarfareProfile.Test.WinnerArm => string.Equals(Arm(f.WinnerTactic), c.Name, StringComparison.OrdinalIgnoreCase),
            WarfareProfile.Test.LoserArm => string.Equals(Arm(f.LoserTactic), c.Name, StringComparison.OrdinalIgnoreCase),
            WarfareProfile.Test.Custom => f.WinnerSide != null && f.WinnerSide.Way.Keeps(c.Name),
            WarfareProfile.Test.GeneralFell => f.GeneralFell,
            WarfareProfile.Test.GeneralCaptured => f.GeneralCaptured,
            WarfareProfile.Test.Declined => f.Declined,
            _ => false,
        };
        return v != c.Negate;
    }

    private static bool AllHold(WarfareProfile profile, WarfareProfile.Beat beat, Facts f)
    {
        foreach (var c in beat.When)
            if (!Holds(profile, c, f)) return false;
        return true;
    }

    /// <summary>One turning point drawn by weight among the eligible beats, then every always-beat whose conditions hold.</summary>
    private static IReadOnlyList<string> Beats(WarfareProfile profile, Facts f, double roll)
    {
        var winnerBeats = f.WinnerTactic >= 0 ? profile.Tactics[f.WinnerTactic].Beats : Array.Empty<string>();
        bool Eligible(WarfareProfile.Beat b)
        {
            // A refused field has no fighting: only the beats that ask for one (a positive <c>declined</c>) can tell it.
            if (b.Always || f.Declined != b.When.Any(c => c.Test == WarfareProfile.Test.Declined && !c.Negate)) return false;
            if (b.TacticBound && Array.FindIndex(winnerBeats, k => string.Equals(k, b.Key, StringComparison.OrdinalIgnoreCase)) < 0) return false;
            return AllHold(profile, b, f);
        }
        var list = new List<string>();
        double total = 0;
        foreach (var b in profile.Beats) if (Eligible(b)) total += b.Weight;
        if (total > 0)
        {
            double target = roll * total;
            string? turning = null;
            foreach (var b in profile.Beats)
            {
                if (!Eligible(b)) continue;
                turning = b.Key; // the last eligible beat absorbs rounding
                target -= b.Weight;
                if (target < 0) break;
            }
            list.Add(turning!);
        }
        foreach (var b in profile.Beats)
            if (b.Always && AllHold(profile, b, f) && !list.Contains(b.Key)) list.Add(b.Key);
        return list;
    }
}
