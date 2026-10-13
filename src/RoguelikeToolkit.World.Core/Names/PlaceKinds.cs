namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Place kind keys accepted by <see cref="FantasyNameGenerator.GeneratePlace"/>; they select which table nouns suit the place. Unrecognised kinds are treated as <see cref="Village"/>.</summary>
public static class PlaceKinds
{
    /// <summary>A small settlement.</summary>
    public const string Village = "village";
    /// <summary>A mid-sized settlement.</summary>
    public const string Town = "town";
    /// <summary>A large settlement (also draws on town words).</summary>
    public const string City = "city";
    /// <summary>A river.</summary>
    public const string River = "river";
    /// <summary>A lake.</summary>
    public const string Lake = "lake";
    /// <summary>A mountain.</summary>
    public const string Mountain = "mountain";
    /// <summary>A hill.</summary>
    public const string Hill = "hill";
    /// <summary>A valley.</summary>
    public const string Valley = "valley";
    /// <summary>A forest.</summary>
    public const string Forest = "forest";
    /// <summary>A fortified stronghold (also draws on town words).</summary>
    public const string Hold = "hold";
    /// <summary>A camp (also draws on hill words).</summary>
    public const string Camp = "camp";
    /// <summary>A sea (also draws on water words).</summary>
    public const string Sea = "sea";

    /// <summary>Every place kind, in declaration order.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Village, Town, City, River, Lake, Mountain, Hill, Valley, Forest, Hold, Camp, Sea,
    };
}
