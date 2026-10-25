using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.Hydrology;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Review section 2 hydrology defects (batch G).</summary>
public class HydrologyIntegrityTests
{
    [Theory]
    [InlineData(4, 4)]
    [InlineData(5, 3)]
    [InlineData(5, 11)]
    public void RiverTerminals_AgreeWithTheGroundTheyEndOn(int size, int seed)
    {
        using var world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
        var hydro = world.Map.DataStore.GetSpan<HydrologyInfo>();
        var kind = world.WaterBodies.Bodies.ToDictionary(b => b.Id, b => b.Kind);
        foreach (var r in world.Rivers.Rivers)
        {
            int tail = r.Path[^1];
            switch (r.Terminal)
            {
                case RiverTerminal.Sink:
                    Assert.True(hydro[tail].Endorheic == 1, $"river {r.Id}: sink on ground that drains to the sea");
                    break;
                case RiverTerminal.DriesOut:
                    Assert.True(hydro[tail].Endorheic == 0, $"river {r.Id}: dries out on endorheic ground");
                    break;
                case RiverTerminal.Sea:
                    if (r.TerminalBody >= 0 && kind.TryGetValue(r.TerminalBody, out var k))
                        Assert.True(k != WaterBodyKind.Lake, $"river {r.Id}: 'sea' terminal is a landlocked lake");
                    break;
            }
        }
    }
}
