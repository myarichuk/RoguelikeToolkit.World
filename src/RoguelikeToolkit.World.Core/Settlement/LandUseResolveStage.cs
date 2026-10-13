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

    /// <summary>Diagnostic name, <c>LandUseResolve</c>.</summary>
    public string Name => "LandUseResolve";
    /// <summary><see cref="Generation.StageOrder.LandUse"/> (99).</summary>
    public int Order => Generation.StageOrder.LandUse;
    /// <summary>Land use and hand-edit layers, which must be registered.</summary>
    public Type[] Reads => new[] { typeof(LandUseInfo), typeof(UserEditInfo) };
    /// <summary>None.</summary>
    public Type[] ReadsOptional => Array.Empty<Type>();
    /// <summary>Land use.</summary>
    public Type[] Writes => new[] { typeof(LandUseInfo) };

    /// <summary>Overwrites every tile pinned with <see cref="UserEditFlags.PinLandUse"/> with the pinned use and activities. Intensity becomes 255, or 0 when the pin clears both use and activities.</summary>
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
