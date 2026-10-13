using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Climate;

/// <summary>
/// Runs the climate model a second time, on the *eroded* relief. The first pass has to precede erosion
/// (erosion needs rainfall for runoff and temperature for ice), but it sees the raw tectonic surface:
/// ranges are higher and sharper than what erosion leaves, so the rain shadows it casts are too deep and
/// the orographic belts too narrow. Hydrology and the biomes should live on the surface the player
/// actually sees, so they read this pass instead.
/// <para>
/// It reuses the configured <see cref="ClimateStage"/> instance (same seed, planet, tunables), so the two
/// passes differ only in the elevation they read. Pure in the stored fields: the climate layer is
/// overwritten wholesale, nothing of the first pass survives except through the eroded height.
/// Lake adjacency still cannot feed moisture, because hydrology has not run yet and a registered but
/// unrun hydrology layer is treated as absent (see <see cref="ClimateStage"/>).
/// </para>
/// Its order ties with erosion (17); the pipeline's sort is stable, so adding it after erosion runs it
/// after erosion and before hydrology (18). <see cref="WorldBuilder.WithClimateRefresh"/> does that.
/// </summary>
[Preserve]
public sealed class ClimateRefreshStage : IWorldGeneratorStage, IDeclaredStage, IStageNamed
{
    private readonly ClimateStage _climate;

    public ClimateRefreshStage(ClimateStage climate)
    {
        _climate = climate ?? throw new ArgumentNullException(nameof(climate));
    }

    public string Name => "ClimateRefresh";
    public int Order => 17;
    public Type[] Reads => new[] { typeof(ElevationInfo), typeof(ClimateInfo) };
    public Type[] ReadsOptional => new[] { typeof(UserEditInfo) };
    public Type[] Writes => new[] { typeof(ClimateInfo) };

    public void Execute(WorldMap map) => _climate.ExecuteRefresh(map);
}
