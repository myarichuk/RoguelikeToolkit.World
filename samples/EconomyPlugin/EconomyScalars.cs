using RoguelikeToolkit.World.Core.History;

namespace EconomyPlugin;

/// <summary>The economy plugin's declared scalars and where they are declared.</summary>
public static class EconomyScalars
{
    /// <summary>The plugin namespace; every operator, scalar and state key starts with it.</summary>
    public const string Namespace = "econ";

    /// <summary>Treasury (0..1000).</summary>
    public const string Wealth = Namespace + "/wealth";
    /// <summary>Tax rate (0..0.6).</summary>
    public const string Tax = Namespace + "/tax";
    /// <summary>Share of revenue lost to graft (0..1).</summary>
    public const string Corruption = Namespace + "/corruption";
    /// <summary>Popular anger (0..1); above 0.5 it leaks into <c>pressure</c>, which Rebellion reads.</summary>
    public const string Unrest = Namespace + "/unrest";
    /// <summary>Share of wealth held by entrenched elites (0..1).</summary>
    public const string Estates = Namespace + "/estates";

    /// <summary>Baseline tax a calm state drifts toward.</summary>
    public const double BaseTax = 0.2;

    /// <summary>Declares the five scalars. Pass to <c>WorldBuilder.WithHistoryScalars</c>.</summary>
    public static void Declare(ScalarRegistry scalars)
    {
        ArgumentNullException.ThrowIfNull(scalars);
        scalars.DeclarePlugin(Wealth, 0, 1000, 50);
        scalars.DeclarePlugin(Tax, 0, 0.6, BaseTax);
        scalars.DeclarePlugin(Corruption, 0, 1, 0.1);
        scalars.DeclarePlugin(Unrest, 0, 1, 0);
        scalars.DeclarePlugin(Estates, 0, 1, 0.3);
    }
}
