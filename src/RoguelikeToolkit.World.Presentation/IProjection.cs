using RoguelikeToolkit.World.Core.Primitives;
namespace RoguelikeToolkit.World.Presentation;

public interface IProjection
{
    Vector2D Project(GeoCoord coord);
    GeoCoord Inverse(Vector2D point);
}
