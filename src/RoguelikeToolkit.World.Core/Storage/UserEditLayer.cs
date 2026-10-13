namespace RoguelikeToolkit.World.Core.Storage;

/// <summary>Coordinate view over the per-tile <see cref="UserEditInfo"/> input layer (an all-zero entry means no edit).</summary>
public class UserEditLayer : StoreLayer<UserEditInfo>
{
    /// <summary>Creates the view over <paramref name="store"/>; see <see cref="StoreLayer{T}"/>.</summary>
    public UserEditLayer(WorldDataStore store) : base(store) { }
}
