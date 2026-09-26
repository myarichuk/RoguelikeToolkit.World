using System.Numerics;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>
/// Marker colors for the visualizer's deposits overlay. Hues are chosen for
/// mutual distinctness on dimmed land, not ore likeness (coal reads pale so it
/// stays visible on a darkened map).
/// </summary>
public static class DepositPalette
{
    public static Vector3 ColorFor(DepositType type) => type switch
    {
        DepositType.Iron => new Vector3(0.70f, 0.35f, 0.20f),
        DepositType.Copper => new Vector3(1.00f, 0.55f, 0.20f),
        DepositType.Gold => new Vector3(1.00f, 0.85f, 0.10f),
        DepositType.Silver => new Vector3(0.85f, 0.88f, 0.92f),
        DepositType.Tin => new Vector3(0.55f, 0.65f, 0.75f),
        DepositType.LeadZinc => new Vector3(0.45f, 0.70f, 0.45f),
        DepositType.Uranium => new Vector3(0.45f, 1.00f, 0.20f),
        DepositType.Coal => new Vector3(0.90f, 0.85f, 0.95f),
        DepositType.Oil => new Vector3(0.75f, 0.45f, 0.95f),
        DepositType.Gas => new Vector3(0.40f, 0.90f, 0.90f),
        DepositType.Salt => new Vector3(1.00f, 1.00f, 1.00f),
        DepositType.Gems => new Vector3(1.00f, 0.30f, 0.80f),
        DepositType.Bauxite => new Vector3(0.85f, 0.30f, 0.25f),
        _ => new Vector3(0.50f, 0.50f, 0.50f),
    };

    /// <summary>Dimmed land context so deposit markers stand out.</summary>
    public static Vector3 DimLand(Vector3 biomeColor) => biomeColor * 0.45f;
}
