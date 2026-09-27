using System;
using System.Numerics;
using RoguelikeToolkit.World.Core;

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
    {
        result = default;

        if (positions == null || positions.Length == 0) return false;

        var mvp = BuildMvp(viewWidth, viewHeight, camera);

        if (!Matrix4x4.Invert(mvp, out Matrix4x4 invMvp)) return false;

        // NDC Coordinates
        float ndcX = (float)((2.0 * mouseX) / viewWidth - 1.0);
        float ndcY = (float)(1.0 - (2.0 * mouseY) / viewHeight); // Invert Y

        Vector4 rayClipNear = new Vector4(ndcX, ndcY, -1.0f, 1.0f);
        Vector4 rayClipFar = new Vector4(ndcX, ndcY, 1.0f, 1.0f);

        Vector4 rayObjNearV = Vector4.Transform(rayClipNear, invMvp);
        Vector4 rayObjFarV = Vector4.Transform(rayClipFar, invMvp);

        if (rayObjNearV.W != 0.0f) { rayObjNearV.X /= rayObjNearV.W; rayObjNearV.Y /= rayObjNearV.W; rayObjNearV.Z /= rayObjNearV.W; }
        if (rayObjFarV.W != 0.0f) { rayObjFarV.X /= rayObjFarV.W; rayObjFarV.Y /= rayObjFarV.W; rayObjFarV.Z /= rayObjFarV.W; }

        float[] rayObjNear = new float[] { rayObjNearV.X, rayObjNearV.Y, rayObjNearV.Z, rayObjNearV.W };
        float[] rayObjFar = new float[] { rayObjFarV.X, rayObjFarV.Y, rayObjFarV.Z, rayObjFarV.W };

        float rayDirX = rayObjFar[0] - rayObjNear[0];
        float rayDirY = rayObjFar[1] - rayObjNear[1];
        float rayDirZ = rayObjFar[2] - rayObjNear[2];

        float len = (float)Math.Sqrt(rayDirX * rayDirX + rayDirY * rayDirY + rayDirZ * rayDirZ);
        rayDirX /= len; rayDirY /= len; rayDirZ /= len;

        float a = rayDirX * rayDirX + rayDirY * rayDirY + rayDirZ * rayDirZ;
        float b = 2.0f * (rayDirX * rayObjNear[0] + rayDirY * rayObjNear[1] + rayDirZ * rayObjNear[2]);
        float c = (rayObjNear[0] * rayObjNear[0] + rayObjNear[1] * rayObjNear[1] + rayObjNear[2] * rayObjNear[2]) - pickRadius * pickRadius;

        float discriminant = b * b - 4 * a * c;

        if (discriminant < 0) return false;

        float t = (-b - (float)Math.Sqrt(discriminant)) / (2.0f * a);
        if (t < 0) return false;

        float hitX = rayObjNear[0] + t * rayDirX;
        float hitY = rayObjNear[1] + t * rayDirY;
        float hitZ = rayObjNear[2] + t * rayDirZ;

        float hitLen = (float)Math.Sqrt(hitX * hitX + hitY * hitY + hitZ * hitZ);
        if (hitLen < 1e-6f) return false;

        float minDistsq = float.MaxValue;
        int nearestIdx = -1;

        for (int i = 0; i < positions.Length / 3; i++)
        {
            float dx = positions[i * 3] - hitX;
            float dy = positions[i * 3 + 1] - hitY;
            float dz = positions[i * 3 + 2] - hitZ;
            float distSq = dx * dx + dy * dy + dz * dz;

            if (distSq < minDistsq)
            {
                minDistsq = distSq;
                nearestIdx = i;
            }
        }

        if (nearestIdx == -1) return false;

        float selX = positions[nearestIdx * 3];
        float selY = positions[nearestIdx * 3 + 1];
        float selZ = positions[nearestIdx * 3 + 2];

        double lat;
        double lon;
        if (projection != null)
        {
            var geo = projection.Inverse(new Vector2D(selX, selY));
            lat = geo.Latitude;
            lon = geo.Longitude;
        }
        else
        {
            // From the normalized pick direction, NOT the mesh vertex: terrain
            // displacement pushes vertices off the unit sphere, and Asin(|z| > 1)
            // is NaN — which used to poison GetTileIndex into returning -1 and
            // crash GetGeoCoord below with IndexOutOfRangeException.
            lat = Math.Asin(Math.Clamp(hitZ / hitLen, -1.0f, 1.0f)) * 180.0 / Math.PI;
            lon = Math.Atan2(hitY, hitX) * 180.0 / Math.PI;
        }

        // Resolve the tile through the store's canonical topology (exact nearest-center
        // search). The previous ring-based heuristic disagreed with the store on
        // ~160/162 tiles at size 2 and routinely displayed the wrong plate/biome.
        int tileIndex = resolveTile(new GeoCoord(lat, lon));

        // A click handler must never throw: a failed pick clears the panel.
        if (tileIndex < 0 || tileIndex >= tileCount) return false;

        result = new HexPickResult(tileIndex, lat, lon, hitX, hitY, hitZ);
        return true;
    }
}
