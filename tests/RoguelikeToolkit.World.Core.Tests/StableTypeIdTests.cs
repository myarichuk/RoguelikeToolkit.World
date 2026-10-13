using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Tests;

public class StableTypeIdTests
{
    private struct Inner { }
    private struct Box<T> { }

    [Fact]
    public void Name_OmitsNamespaces_ButKeepsNestingAndGenericArguments()
    {
        Assert.Equal("Vector3D", StableTypeId.Name(typeof(Vector3D)));
        Assert.Equal("StableTypeIdTests+Inner", StableTypeId.Name(typeof(Inner)));
        Assert.Equal("StableTypeIdTests+Box`1[Vector3D]", StableTypeId.Name(typeof(Box<Vector3D>)));
    }

    [Fact]
    public void Hash_IsPinned()
    {
        // A change here means every persisted store/entity file written so far stops loading.
        Assert.Equal(0x1C27586B6907FB85ul, StableTypeId.Hash(typeof(Vector3D)));
    }
}
