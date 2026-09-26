using Xunit;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Presentation;
using WorldType = RoguelikeToolkit.World.Core.World;

namespace RoguelikeToolkit.World.Presentation.Tests;

public class TolkienSvgRendererTests
{
    private static WorldType BuildSmallWorld(int seed = 42)
        => new WorldBuilder().WithSize(1).WithSeed(seed).WithPlateCount(6).Build();

    [Fact]
    public void Render_ReturnsFramedSvgDocument()
    {
        using var world = BuildSmallWorld();
        string svg = TolkienSvgRenderer.Render(world);

        Assert.StartsWith("<?xml", svg);
        Assert.Contains("<svg", svg);
        Assert.Contains("#e9d9a6", svg); // parchment ground
        Assert.Contains("THE KNOWN WORLD", svg); // default title, Tolkien caps
        Assert.True(svg.TrimEnd().EndsWith("</svg>"));
    }

    [Fact]
    public void Render_IsDeterministic()
    {
        using var world = BuildSmallWorld();
        Assert.Equal(TolkienSvgRenderer.Render(world), TolkienSvgRenderer.Render(world));
    }

    [Fact]
    public void Render_DrawsLandmassAndTitle()
    {
        using var world = BuildSmallWorld();
        var svg = TolkienSvgRenderer.Render(world,
            new TolkienSvgOptions { Title = "Beleriand & Beyond <test>" });

        Assert.Contains("<path", svg); // landmass subpaths
        Assert.Contains("BELERIAND &amp; BEYOND &lt;TEST&gt;", svg); // escaped caps title
    }

    [Fact]
    public void Render_EmptyWorld_StillFramesParchment()
    {
        using var world = BuildSmallWorld(seed: 7);
        var svg = TolkienSvgRenderer.Render(world,
            new TolkienSvgOptions { DrawRivers = false, DrawForests = false, DrawMountains = false });
        Assert.Contains("<svg", svg);
        Assert.True(svg.TrimEnd().EndsWith("</svg>"));
    }
}
