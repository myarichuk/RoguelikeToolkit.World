using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>Display options for the Tolkien-style parchment export.</summary>
public sealed class TolkienSvgOptions
{
    public int Width { get; set; } = 1600;
    public int Height { get; set; } = 800;
    public string Title { get; set; } = "The Known World";
    public string Subtitle { get; set; } = string.Empty;
    public bool DrawRivers { get; set; } = true;
    public bool DrawForests { get; set; } = true;
    public bool DrawMountains { get; set; } = true;
    public bool DrawCompass { get; set; } = true;

    // Parchment + ink palette.
    public string Parchment { get; set; } = "#e9d9a6";
    public string ParchmentDark { get; set; } = "#dcc78e";
    public string Ink { get; set; } = "#4a3b28";
    public string WaterInk { get; set; } = "#3d5a80";
}

/// <summary>
/// Renders a <see cref="Core.World"/> as a Tolkien-style parchment map in SVG:
/// ink coastlines from tile hex corners, triangular mountain glyphs, tree
/// glyphs for woodlands, ink rivers, a title cartouche, and a compass rose.
/// Equirectangular projection; no external geometry or font dependencies.
/// </summary>
public static class TolkienSvgRenderer
{
    public static string Render(Core.World world, TolkienSvgOptions? options = null)
    {
        var opt = options ?? new TolkienSvgOptions();
        var store = world.Map.DataStore;
        int n = store.TileCount;
        var elev = store.GetSpan<ElevationInfo>();
        var locals = store.GetSpan<LocalMapInfo>();
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;

        double w = Math.Max(64, opt.Width);
        double h = Math.Max(64, opt.Height);

        // Projected tile centers + water mask.
        var px = new double[n];
        var py = new double[n];
        var water = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var g = store.GetGeoCoord(i);
            px[i] = (g.Longitude + 180.0) / 360.0 * w;
            py[i] = (90.0 - g.Latitude) / 180.0 * h;
            water[i] = elev[i].Height < ElevationGenerationStage.SeaLevel
                || (hasHydro && hydro[i].LakeDepth > 0f);
        }

        var sb = new StringBuilder(1 << 16);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"")
            .Append(w.ToString("F0", CultureInfo.InvariantCulture))
            .Append("\" height=\"").Append(h.ToString("F0", CultureInfo.InvariantCulture))
            .Append("\" viewBox=\"0 0 ").Append(w.ToString("F0", CultureInfo.InvariantCulture))
            .Append(' ').Append(h.ToString("F0", CultureInfo.InvariantCulture)).Append("\">\n");

        // Parchment ground + aged edge vignette.
        sb.Append("<rect x=\"0\" y=\"0\" width=\"100%\" height=\"100%\" fill=\"").Append(opt.Parchment).Append("\"/>\n");
        sb.Append("<rect x=\"8\" y=\"8\" width=\"").Append(F(w - 16)).Append("\" height=\"").Append(F(h - 16))
            .Append("\" fill=\"none\" stroke=\"").Append(opt.Ink).Append("\" stroke-width=\"3\"/>\n");
        sb.Append("<rect x=\"16\" y=\"16\" width=\"").Append(F(w - 32)).Append("\" height=\"").Append(F(h - 32))
            .Append("\" fill=\"none\" stroke=\"").Append(opt.Ink).Append("\" stroke-width=\"1\"/>\n");

        // Landmass: one stroked subpath per land tile from hex corners.
        sb.Append("<path d=\"");
        Span<int> neighbors = stackalloc int[6];
        var cornerIdx = new int[6];
        for (int i = 0; i < n; i++)
        {
            if (water[i]) continue;
            int m = HexCorners(store, i, neighbors, cornerIdx, w, h, out var pts);
            if (m < 3) continue;
            sb.Append('M').Append(F(pts[0].X)).Append(' ').Append(F(pts[0].Y));
            for (int k = 1; k < m; k++)
                sb.Append('L').Append(F(pts[k].X)).Append(' ').Append(F(pts[k].Y));
            sb.Append('Z');
        }
        sb.Append("\" fill=\"").Append(opt.ParchmentDark).Append("\" stroke=\"").Append(opt.Ink)
            .Append("\" stroke-width=\"1.4\" stroke-linejoin=\"round\"/>\n");

        // Mountain glyphs: single ink triangles (canyons get a slash).
        if (opt.DrawMountains)
        {
            sb.Append("<g fill=\"none\" stroke=\"").Append(opt.Ink).Append("\" stroke-width=\"1.6\" stroke-linejoin=\"round\">\n");
            for (int i = 0; i < n; i++)
            {
                if (water[i]) continue;
                var biome = locals[i].Biome;
                if (biome != BiomeType.Mountain && biome != BiomeType.Canyon) continue;
                double x = px[i], y = py[i];
                sb.Append("<path d=\"M").Append(F(x - 5)).Append(' ').Append(F(y + 4))
                    .Append('L').Append(F(x)).Append(' ').Append(F(y - 5))
                    .Append('L').Append(F(x + 5)).Append(' ').Append(F(y + 4)).Append("Z\"/>\n");
                if (biome == BiomeType.Canyon)
                    sb.Append("<path d=\"M").Append(F(x - 3)).Append(' ').Append(F(y + 7))
                        .Append('L').Append(F(x + 3)).Append(' ').Append(F(y - 7)).Append("\"/>\n");
            }
            sb.Append("</g>\n");
        }

        // Woodland glyphs: conifer triangles, jungle canopy circles, swamp tufts.
        if (opt.DrawForests)
        {
            sb.Append("<g fill=\"none\" stroke=\"").Append(opt.Ink).Append("\" stroke-width=\"1.2\">\n");
            for (int i = 0; i < n; i++)
            {
                if (water[i]) continue;
                double x = px[i], y = py[i];
                switch (locals[i].Biome)
                {
                    case BiomeType.Forest:
                        sb.Append("<path d=\"M").Append(F(x)).Append(' ').Append(F(y - 5))
                            .Append('L').Append(F(x - 4)).Append(' ').Append(F(y + 2))
                            .Append('L').Append(F(x + 4)).Append(' ').Append(F(y + 2))
                            .Append("Z M").Append(F(x)).Append(' ').Append(F(y + 2))
                            .Append('L').Append(F(x)).Append(' ').Append(F(y + 5)).Append("\"/>\n");
                        break;
                    case BiomeType.Jungle:
                        sb.Append("<circle cx=\"").Append(F(x)).Append("\" cy=\"").Append(F(y - 1))
                            .Append("\" r=\"3.4\"/><path d=\"M").Append(F(x)).Append(' ').Append(F(y + 2))
                            .Append('L').Append(F(x)).Append(' ').Append(F(y + 5)).Append("\"/>\n");
                        break;
                    case BiomeType.Swamp:
                        sb.Append("<path d=\"M").Append(F(x - 3)).Append(' ').Append(F(y + 4))
                            .Append('L').Append(F(x - 3)).Append(' ').Append(F(y - 1))
                            .Append(" M").Append(F(x)).Append(' ').Append(F(y + 4))
                            .Append('L').Append(F(x)).Append(' ').Append(F(y - 2))
                            .Append(" M").Append(F(x + 3)).Append(' ').Append(F(y + 4))
                            .Append('L').Append(F(x + 3)).Append(' ').Append(F(y - 1)).Append("\"/>\n");
                        break;
                }
            }
            sb.Append("</g>\n");
        }

        // Rivers as ink-water polylines through tile centers.
        if (opt.DrawRivers)
        {
            sb.Append("<g fill=\"none\" stroke=\"").Append(opt.WaterInk)
                .Append("\" stroke-width=\"1.4\" opacity=\"0.85\" stroke-linecap=\"round\">\n");
            foreach (var river in world.Rivers.Rivers)
            {
                if (river.Path.Count < 2) continue;
                sb.Append("<path d=\"M");
                for (int k = 0; k < river.Path.Count; k++)
                {
                    int t = river.Path[k];
                    if ((uint)t >= (uint)n) continue;
                    if (k > 0) sb.Append('L');
                    sb.Append(F(px[t])).Append(' ').Append(F(py[t]));
                }
                sb.Append("\"/>\n");
            }
            sb.Append("</g>\n");
        }

        if (opt.DrawCompass)
            Compass(sb, w - 90, h - 90, opt);

        // Title cartouche.
        double titleSize = Math.Max(18, w / 44.0);
        sb.Append("<g text-anchor=\"middle\" font-family=\"Georgia, 'Times New Roman', serif\" fill=\"")
            .Append(opt.Ink).Append("\">\n");
        sb.Append("<text x=\"").Append(F(w / 2)).Append("\" y=\"").Append(F(48))
            .Append("\" font-size=\"").Append(F(titleSize))
            .Append("\" letter-spacing=\"6\">").Append(Escape(opt.Title.ToUpperInvariant())).Append("</text>\n");
        string sub = string.IsNullOrWhiteSpace(opt.Subtitle)
            ? $"Seed {world.Seed} - {n:N0} tiles"
            : opt.Subtitle;
        sb.Append("<text x=\"").Append(F(w / 2)).Append("\" y=\"").Append(F(48 + titleSize * 0.9))
            .Append("\" font-size=\"").Append(F(titleSize * 0.42))
            .Append("\" letter-spacing=\"3\">").Append(Escape(sub)).Append("</text>\n");
        sb.Append("</g>\n");

        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private struct Pt { public double X; public double Y; }

    /// <summary>
    /// Approximate spherical hex corners from the tile center and its ring of
    /// neighbors: corner k is the normalized (center + nb[k] + nb[k+1]) point.
    /// Returns the corner count and fills pts (projected).
    /// </summary>
    private static int HexCorners(
        WorldDataStore store, int tile, Span<int> neighbors, int[] order,
        double renderW, double renderH, out Pt[] pts)
    {
        pts = new Pt[6];
        var vectors = store.GetTileVectors();
        var c = vectors[tile];
        int m = store.GetAdjacent(tile, neighbors);
        if (m < 3) return 0;

        // Tangent basis at the tile center.
        var ax = Math.Abs(c.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
        double d = Vector3D.Dot(ax, c);
        var u = new Vector3D(ax.X - d * c.X, ax.Y - d * c.Y, ax.Z - d * c.Z).Normalize();
        var v = new Vector3D(
            c.Y * u.Z - c.Z * u.Y,
            c.Z * u.X - c.X * u.Z,
            c.X * u.Y - c.Y * u.X);

        var angles = new double[6];
        for (int k = 0; k < m; k++)
        {
            int nb = neighbors[k];
            order[k] = nb;
            var o = vectors[nb];
            double ox = o.X - c.X, oy = o.Y - c.Y, oz = o.Z - c.Z;
            angles[k] = Math.Atan2(ox * v.X + oy * v.Y + oz * v.Z, ox * u.X + oy * u.Y + oz * u.Z);
        }
        Array.Sort(angles, order, 0, m);

        // Project corners. Longitude wraps at the dateline: keep the polygon
        // near the tile center by unwrapping corner longitudes.
        var geo = store.GetGeoCoord(tile);
        for (int k = 0; k < m; k++)
        {
            var a = vectors[order[k]];
            var b = vectors[order[(k + 1) % m]];
            var corner = new Vector3D(a.X + b.X + c.X, a.Y + b.Y + c.Y, a.Z + b.Z + c.Z).Normalize();
            var g = corner.ToGeoCoord();
            double lon = g.Longitude;
            while (lon - geo.Longitude > 180) lon -= 360;
            while (lon - geo.Longitude < -180) lon += 360;
            // Dateline tiles unwrap past the frame edge; clamp so edge
            // landmass squashes against the border instead of overflowing.
            pts[k] = new Pt
            {
                X = Math.Clamp((lon + 180.0) / 360.0 * renderW, 0.0, renderW),
                Y = (90.0 - g.Latitude) / 180.0 * renderH
            };
        }
        return m;
    }

    private static void Compass(StringBuilder sb, double cx, double cy, TolkienSvgOptions opt)
    {
        sb.Append("<g stroke=\"").Append(opt.Ink).Append("\" fill=\"none\" stroke-width=\"1.4\">\n");
        sb.Append("<circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy)).Append("\" r=\"26\"/>\n");
        sb.Append("<path d=\"M").Append(F(cx)).Append(' ').Append(F(cy - 26))
            .Append('L').Append(F(cx + 6)).Append(' ').Append(F(cy))
            .Append('L').Append(F(cx)).Append(' ').Append(F(cy + 26))
            .Append('L').Append(F(cx - 6)).Append(' ').Append(F(cy)).Append("Z\"/>\n");
        sb.Append("<path d=\"M").Append(F(cx - 26)).Append(' ').Append(F(cy))
            .Append('L').Append(F(cx)).Append(' ').Append(F(cy - 6))
            .Append('L').Append(F(cx + 26)).Append(' ').Append(F(cy))
            .Append('L').Append(F(cx)).Append(' ').Append(F(cy + 6)).Append("Z\"/>\n");
        sb.Append("</g>\n");
        sb.Append("<text x=\"").Append(F(cx)).Append("\" y=\"").Append(F(cy - 30))
            .Append("\" text-anchor=\"middle\" font-family=\"Georgia, serif\" font-size=\"14\" fill=\"")
            .Append(opt.Ink).Append("\">N</text>\n");
    }

    private static string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;");
}
