namespace RoguelikeToolkit.World.Core.Names;

public static class PlaceKinds
{
    public const string Village = "village";
    public const string Town = "town";
    public const string City = "city";
    public const string River = "river";
    public const string Lake = "lake";
    public const string Mountain = "mountain";
    public const string Hill = "hill";
    public const string Valley = "valley";
    public const string Forest = "forest";
    public const string Hold = "hold";
    public const string Camp = "camp";
    public const string Sea = "sea";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Village, Town, City, River, Lake, Mountain, Hill, Valley, Forest, Hold, Camp, Sea,
    };
}
