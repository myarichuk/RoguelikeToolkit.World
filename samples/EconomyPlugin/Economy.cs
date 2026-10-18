using RoguelikeToolkit.World.Core.History;

namespace EconomyPlugin;

/// <summary>Entry point: <c>HistoryPluginHost.Register(Economy.Register, Economy.DeclareScalars)</c>.</summary>
public static class Economy
{
    /// <summary>Declares the economy scalars. Pass to <c>WorldBuilder.WithHistoryScalars</c>.</summary>
    public static void DeclareScalars(ScalarRegistry scalars) => EconomyScalars.Declare(scalars);

    /// <summary>Registers the economy and fiscal operators.</summary>
    public static void Register(HistoryDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        driver.AddPluginOperator(new EconomyOperator());
        driver.AddPluginOperator(new FiscalOperator());
    }
}
