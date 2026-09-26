using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Presentation;

namespace RoguelikeToolkit.World.App;

/// <summary>
/// Lightweight 2D equirectangular preview of a generated world: parchment
/// ground, land discs, mountain triangles, woodland dots, and ink rivers.
/// The pretty output stays <see cref="TolkienSvgRenderer"/> (SVG export).
/// </summary>
public class MapPreviewControl : Control
{
    public static readonly StyledProperty<Core.World?> WorldProperty =
        AvaloniaProperty.Register<MapPreviewControl, Core.World?>(nameof(World));

    public Core.World? World
    {
        get => GetValue(WorldProperty);
        set => SetValue(WorldProperty, value);
    }

    private static readonly SolidColorBrush Parchment = new(Color.Parse("#e9d9a6"));
    private static readonly SolidColorBrush Land = new(Color.Parse("#c9b078"));
    private static readonly SolidColorBrush Ink = new(Color.Parse("#4a3b28"));
    private static readonly SolidColorBrush WaterInk = new(Color.Parse("#3d5a80"));
    private static readonly SolidColorBrush Canopy = new(Color.Parse("#5a6e3c"));

    static MapPreviewControl()
    {
        WorldProperty.Changed.AddClassHandler<MapPreviewControl>((c, _) => c.InvalidateVisual());
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.FillRectangle(Parchment, bounds);
        var world = World;
        if (world == null)
        {
            var hint = new FormattedText("Generate a world to preview it here.",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, 14, Ink);
            context.DrawText(hint, new Point(16, 16));
            return;
        }

        var store = world.Map.DataStore;
        int n = store.TileCount;
        var elev = store.GetSpan<ElevationInfo>();
        var locals = store.GetSpan<LocalMapInfo>();

        double w = Math.Max(1, bounds.Width);
        double h = Math.Max(1, bounds.Height);
        double dot = Math.Clamp(360.0 / Math.Sqrt(Math.Max(1, n)) * (w / 360.0) / 2.0, 1.5, 10.0);

        // Land discs first (water stays parchment).
        for (int i = 0; i < n; i++)
        {
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            var g = store.GetGeoCoord(i);
            double x = (g.Longitude + 180.0) / 360.0 * w;
            double y = (90.0 - g.Latitude) / 180.0 * h;
            var biome = locals[i].Biome;
            var brush = biome is BiomeType.Forest or BiomeType.Jungle or BiomeType.Swamp ? Canopy : Land;
            context.FillRectangle(brush, new Rect(x - dot, y - dot, dot * 2, dot * 2));
        }

        // Mountain triangles.
        foreach (int i in MountainTiles(store, locals, n))
        {
            var g = store.GetGeoCoord(i);
            double x = (g.Longitude + 180.0) / 360.0 * w;
            double y = (90.0 - g.Latitude) / 180.0 * h;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(x - 4, y + 3), true);
                ctx.LineTo(new Point(x, y - 4));
                ctx.LineTo(new Point(x + 4, y + 3));
                ctx.EndFigure(true);
            }
            context.DrawGeometry(null, new Pen(Ink, 1.2), geo);
        }

        // Rivers.
        var pen = new Pen(WaterInk, 1.2);
        foreach (var river in world.Rivers.Rivers)
        {
            if (river.Path.Count < 2) continue;
            Point? prev = null;
            foreach (int t in river.Path)
            {
                if ((uint)t >= (uint)n) continue;
                var g = store.GetGeoCoord(t);
                var p = new Point((g.Longitude + 180.0) / 360.0 * w, (90.0 - g.Latitude) / 180.0 * h);
                if (prev.HasValue) context.DrawLine(pen, prev.Value, p);
                prev = p;
            }
        }
    }

    private static System.Collections.Generic.List<int> MountainTiles(
        WorldDataStore store, System.Span<LocalMapInfo> locals, int n)
    {
        var list = new System.Collections.Generic.List<int>();
        var elev = store.GetSpan<ElevationInfo>();
        for (int i = 0; i < n; i++)
        {
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            if (locals[i].Biome == BiomeType.Mountain || locals[i].Biome == BiomeType.Canyon)
                list.Add(i);
        }
        return list;
    }
}
