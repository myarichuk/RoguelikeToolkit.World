namespace RoguelikeToolkit.World.Core.Lithosphere;

/// <summary>Prospectable resources seeded from tectonic, hydrologic and climate setting.</summary>
public enum DepositType : byte
{
    /// <summary>Banded iron formation on old craton interiors.</summary>
    Iron = 0,
    /// <summary>Porphyry and hydrothermal copper in orogens and island arcs; volcanogenic sulfide in rifts and emergent ridges.</summary>
    Copper = 1,
    /// <summary>Lode gold in fold belts and arcs, richer where a river works it into placers; greenstone and paleoplacer gold on very old cratons.</summary>
    Gold = 2,
    /// <summary>Epithermal silver in magmatic belts; by-product silver of rift sulfides.</summary>
    Silver = 3,
    /// <summary>Tin granite in collisional highlands (orogens above height 0.30).</summary>
    Tin = 4,
    /// <summary>Veins in fold belts and arcs, rift sulfides (the richest), and carbonate-foreland deposits just beyond a convergent belt.</summary>
    LeadZinc = 5,
    /// <summary>Unconformity and placer uranium on cratons; roll-front uranium in dry, low sedimentary platforms.</summary>
    Uranium = 6,
    /// <summary>Coal measures in warm, wet continental lowlands outside fold belts.</summary>
    Coal = 7,
    /// <summary>Oil on continental shelves and in low onshore platform basins, richer on rifted margins and near rivers.</summary>
    Oil = 8,
    /// <summary>Gas in the same basins as oil, favouring deeper burial on the shelf.</summary>
    Gas = 9,
    /// <summary>Halite on the saline sump of an endorheic basin where evaporation exceeds rainfall.</summary>
    Salt = 10,
    /// <summary>Metamorphic gems in strongly uplifted orogens; kimberlite-pipe diamonds through ancient cratons.</summary>
    Gems = 11,
    /// <summary>Bauxite from hot, wet weathering of old, flat, stable continental surfaces.</summary>
    Bauxite = 12,
    /// <summary>Calcium sulfate evaporite: precipitates first, on the margin of a drying basin, before halite.</summary>
    Gypsum = 13
}
