using System;
using System.Numerics;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>Camera state for hex picking, mirroring the visualizer view parameters.</summary>
public readonly record struct PickCamera(float YawDegrees, float PitchDegrees, float PanX, float PanY, float Distance);

/// <summary>Result of a hex pick: tile resolved through canonical topology plus hit data.</summary>
public readonly record struct HexPickResult(int TileIndex, double Latitude, double Longitude, float HitX, float HitY, float HitZ);

/// <summary>
/// Pure hex-picking math shared by the visualizer: NDC unprojection through the
/// render matrix, ray/sphere intersection, then resolution through canonical
/// topology. Kept free of UI/GL types so it is unit-testable; the GL control
/// is a thin adapter over this.
/// </summary>
public static class HexPicker
{
    public static Matrix4x4 BuildMvp(double viewWidth, double viewHeight, PickCamera camera)
    {
        float aspect = (float)(viewWidth / viewHeight);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(45.0f * (float)Math.PI / 180.0f, aspect, 0.1f, 100.0f);
        var view = Matrix4x4.CreateTranslation(-camera.PanX, -camera.PanY, -camera.Distance);
        var modelX = Matrix4x4.CreateRotationX(camera.PitchDegrees * (float)Math.PI / 180.0f);
        var modelY = Matrix4x4.CreateRotationY(camera.YawDegrees * (float)Math.PI / 180.0f);

        var model = modelX * modelY;
        var viewProj = view * projection;
        return model * viewProj;
    }

    /// <summary>Picks against a fixed sphere of <paramref name="pickRadius"/> (globe) or the z=0 map plane (when <paramref name="projection"/> is set).</summary>
    public static bool TryPick(
        double mouseX,
        double mouseY,
        double viewWidth,
        double viewHeight,
        PickCamera camera,
        float pickRadius,
        float[] positions,
        IProjection? projection,
        Func<GeoCoord, int> resolveTile,
        int tileCount,
        out HexPickResult result)
        => TryPick(mouseX, mouseY, viewWidth, viewHeight, camera, pickRadius, positions, projection, resolveTile, tileCount, null, out result);

    /// <summary>
    /// Resolves the tile under the mouse. Flat maps (<paramref name="projection"/> set) intersect the z=0 plane they are
    /// drawn on and invert the projection at the hit. The globe intersects a sphere: of <paramref name="pickRadius"/>, or,
    /// for displaced terrain, of each candidate tile's own radius from <paramref name="surfaceRadius"/>, starting at
    /// <paramref name="pickRadius"/> (an upper bound on the relief) and re-intersecting until the tile stops changing.
    /// </summary>
    public static bool TryPick(
        double mouseX,
        double mouseY,
        double viewWidth,
        double viewHeight,
        PickCamera camera,
        float pickRadius,
        float[] positions,
        IProjection? projection,
        Func<GeoCoord, int> resolveTile,
        int tileCount,
        Func<int, float>? surfaceRadius,
        out HexPickResult result)
    {
        result = default;

        if (positions == null || positions.Length == 0) return false;

        var mvp = BuildMvp(viewWidth, viewHeight, camera);
        if (!Matrix4x4.Invert(mvp, out Matrix4x4 invMvp)) return false;

        float ndcX = (float)((2.0 * mouseX) / viewWidth - 1.0);
        float ndcY = (float)(1.0 - (2.0 * mouseY) / viewHeight); // Invert Y

        var near = Unproject(new Vector4(ndcX, ndcY, -1.0f, 1.0f), invMvp);
        var far = Unproject(new Vector4(ndcX, ndcY, 1.0f, 1.0f), invMvp);
        var dir = far - near;
        float len = dir.Length();
        if (!(len > 0f)) return false;
        dir /= len;

        if (projection != null)
        {
            // Flat maps lie on z = 0: intersect that plane, not a sphere (a sphere hit sits in front of the plane and
            // perspective pulls it towards the centre; beyond the unit circle it misses outright).
            if (Math.Abs(dir.Z) < 1e-9f) return false;
            float tPlane = -near.Z / dir.Z;
            if (tPlane < 0f) return false;
            var hit = near + dir * tPlane;
            var geo = projection.Inverse(new Vector2D(hit.X, hit.Y));
            // The inverses do not wrap: off the map's edge they return longitudes past 180 or latitudes past 90. A hair
            // over is still on the map: unprojecting through a float matrix loses ~1e-4 units, a few hundredths of a degree.
            const double Slack = 0.05;
            if (!double.IsFinite(geo.Latitude) || !double.IsFinite(geo.Longitude)
                || Math.Abs(geo.Latitude) > 90.0 + Slack || Math.Abs(geo.Longitude) > 180.0 + Slack) return false;
            return Resolve(Math.Clamp(geo.Latitude, -90.0, 90.0), Math.Clamp(geo.Longitude, -180.0, 180.0), hit, resolveTile, tileCount, out result);
        }

        if (!IntersectSphere(near, dir, pickRadius, out var sphereHit)) return false;
        if (!ToLatLon(sphereHit, out double lat, out double lon)) return false;
        int tile = resolveTile(new GeoCoord(lat, lon));
        if (surfaceRadius != null && (uint)tile < (uint)tileCount)
        {
            // Displaced terrain: the outer sphere's hit lies above the ground and towards the limb. Walk down onto the
            // candidate tile's own surface; a few rounds settle except on a cliff edge, where either side is a fair answer.
            for (int round = 0; round < 6; round++)
            {
                if (!IntersectSphere(near, dir, surfaceRadius(tile), out var h) || !ToLatLon(h, out double la, out double lo)) break;
                int next = resolveTile(new GeoCoord(la, lo));
                if ((uint)next >= (uint)tileCount) break;
                sphereHit = h; lat = la; lon = lo;
                if (next == tile) break;
                tile = next;
            }
        }
        return Resolve(lat, lon, sphereHit, resolveTile, tileCount, out result);
    }

    private static Vector3 Unproject(Vector4 clip, Matrix4x4 inv)
    {
        var v = Vector4.Transform(clip, inv);
        return v.W != 0f ? new Vector3(v.X / v.W, v.Y / v.W, v.Z / v.W) : new Vector3(v.X, v.Y, v.Z);
    }

    private static bool IntersectSphere(Vector3 origin, Vector3 dir, float radius, out Vector3 hit)
    {
        hit = default;
        float b = 2.0f * Vector3.Dot(dir, origin);
        float c = origin.LengthSquared() - radius * radius;
        float discriminant = b * b - 4f * c;
        if (discriminant < 0f) return false;
        float t = (-b - (float)Math.Sqrt(discriminant)) / 2f;
        if (t < 0f) return false;
        hit = origin + dir * t;
        return true;
    }

    private static bool ToLatLon(Vector3 hit, out double lat, out double lon)
    {
        float hitLen = hit.Length();
        lat = lon = 0;
        if (hitLen < 1e-6f) return false;
        // From the hit direction, not a mesh vertex: displaced vertices sit off the unit sphere.
        lat = Math.Asin(Math.Clamp(hit.Z / hitLen, -1.0f, 1.0f)) * 180.0 / Math.PI;
        lon = Math.Atan2(hit.Y, hit.X) * 180.0 / Math.PI;
        return double.IsFinite(lat) && double.IsFinite(lon);
    }

    private static bool Resolve(double lat, double lon, Vector3 hit, Func<GeoCoord, int> resolveTile, int tileCount, out HexPickResult result)
    {
        result = default;
        // Resolve through the store's canonical topology (exact nearest-center search).
        int tileIndex = resolveTile(new GeoCoord(lat, lon));
        // A click handler must never throw: a failed pick clears the panel.
        if (tileIndex < 0 || tileIndex >= tileCount) return false;
        result = new HexPickResult(tileIndex, lat, lon, hit.X, hit.Y, hit.Z);
        return true;
    }
}
