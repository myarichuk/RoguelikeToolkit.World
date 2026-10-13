using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>
/// The last stage of a land-use pipeline: applies hand-painted pins (<see cref="UserEditFlags.PinLandUse"/>)
/// over whatever the allocation stages decided, so an edit always wins and a plugin never has to know about
/// the editor. Order 99.
/// </summary>
[Preserve]
public sealed class LandUseResolveStage : IWorldGeneratorStage, IDeclaredStage, IStageNamed
{
    public const int StageOrder = 99;

    public string Name => "LandUseResolve";
    public int Order => StageOrder;
    public Type[] Reads => new[] { typeof(LandUseInfo), typeof(UserEditInfo) };
    public Type[] ReadsOptional => Array.Empty<Type>();
    public Type[] Writes => new[] { typeof(LandUseInfo) };

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var use = store.GetSpan<LandUseInfo>();
        var pins = store.GetSpan<UserEditInfo>();
        for (int i = 0; i < use.Length; i++)
        {
            if ((pins[i].Flags & UserEditFlags.PinLandUse) == 0) continue;
            use[i] = new LandUseInfo
            {
                Use = pins[i].LandUse,
                Activities = pins[i].LandActivities,
                Intensity = pins[i].LandUse == LandUseId.None && pins[i].LandActivities == 0 ? (byte)0 : (byte)255,
            };
        }
    }
}
