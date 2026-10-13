using System;

namespace RoguelikeToolkit.World.Core;

public class UserEditLayer : StoreLayer<UserEditInfo>
{
    public UserEditLayer(WorldDataStore store) : base(store) { }
}
