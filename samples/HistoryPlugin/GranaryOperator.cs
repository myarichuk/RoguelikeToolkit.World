using RoguelikeToolkit.World.Core.History;

namespace HistoryPlugin;

/// <summary>
/// A minimal history operator: every polity keeps a grain reserve that refills each turn and drains with
/// crowding. A starving polity (empty reserve) gets extra pressure, which the built-in Rebellion and
/// Collapse rules already read. It only uses the public API: a read-only <see cref="TurnView"/> in,
/// <see cref="ProposalSink"/> proposals out.
/// </summary>
public sealed class GranaryOperator : IHistoryOperator
{
    /// <summary>The plugin namespace; operator, scalar and state-bag names all start with it.</summary>
    public const string Namespace = "sample";

    /// <summary>Name of the declared grain scalar.</summary>
    public const string GrainScalar = Namespace + "/grain";

    public const double MaxGrain = 100;

    public string Name => Namespace + "/granary";

    // Inside the reserved plugin band (HistoryNaming.PluginOrderMin..PluginOrderMax): after the built-ins.
    public int Order => 500;

    /// <summary>Declares the plugin's scalars. Pass to <c>WorldBuilder.WithHistoryScalars</c>.</summary>
    public static void DeclareScalars(ScalarRegistry scalars)
        => scalars.DeclarePlugin(GrainScalar, 0, MaxGrain, MaxGrain / 2);

    /// <summary>Registers the operator. Pass to <c>WorldBuilder.WithHistoryDriver</c>.</summary>
    public static void Register(HistoryDriver driver) => driver.AddPluginOperator(new GranaryOperator());

    public void Propose(TurnView view, ProposalSink sink)
    {
        if (!view.Scalars.TryGet(GrainScalar, out var grain)) return; // host did not declare it: do nothing
        foreach (var polity in view.Polities)
        {
            if (!polity.Alive) continue;
            double pressure = polity.Scalars[ScalarRegistry.Pressure];
            double stock = polity.Scalars[grain.Id];
            // Harvest of 2, minus a share of the crowding; the commit clamps the result to 0..MaxGrain.
            sink.Adjust(polity.Id, grain.Id, 2 - pressure * 0.05);
            if (stock <= 0) sink.Adjust(polity.Id, ScalarRegistry.Pressure, 1);
        }
    }
}
