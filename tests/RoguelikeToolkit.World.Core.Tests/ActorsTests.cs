using RoguelikeToolkit.World.Core.Actors;
using RoguelikeToolkit.World.Core.Lineage;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class ActorsTests
{
    private static readonly TraitWeight[] Balanced =
        { TraitWeight.Balanced, TraitWeight.Balanced, TraitWeight.Balanced, TraitWeight.Balanced, TraitWeight.Balanced };

    private static Actor Make(int seed, int id, int maturation = 16, int age = 30) => Actor.Create(
        seed, new ActorId(id), "human",
        Ancestry.From(new AncestryShare("human", 1.0)), Balanced, maturation, age);

    [Fact]
    public void SameSeedAndId_RollsSameTraits()
    {
        var a = ActorTraits.Roll(42, new ActorId(7), Balanced);
        var b = ActorTraits.Roll(42, new ActorId(7), Balanced);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Priors_BoundTheRoll()
    {
        var low = ActorTraits.Roll(9, new ActorId(3),
            new[] { TraitWeight.Low, TraitWeight.Low, TraitWeight.Low, TraitWeight.Low, TraitWeight.Low });
        var high = ActorTraits.Roll(9, new ActorId(3),
            new[] { TraitWeight.High, TraitWeight.High, TraitWeight.High, TraitWeight.High, TraitWeight.High });
        foreach (var v in new[] { low.Openness, low.Conscientiousness, low.Extraversion, low.Agreeableness, low.Neuroticism })
            Assert.True(v < 128, $"low prior rolled {v}");
        foreach (var v in new[] { high.Openness, high.Conscientiousness, high.Extraversion, high.Agreeableness, high.Neuroticism })
            Assert.True(v >= 128, $"high prior rolled {v}");
        Assert.NotEqual(low, high);
    }

    [Fact]
    public void AdultFlag_ComesFromMaturationAge()
    {
        Assert.True(Make(1, 1, maturation: 16, age: 16).IsAdult);
        Assert.True(Make(1, 1, maturation: 16, age: 40).IsAdult);
        Assert.False(Make(1, 1, maturation: 16, age: 15).IsAdult);
    }

    [Fact]
    public void RequireAdult_RejectsChildren()
    {
        var adult = Make(1, 1, age: 30);
        adult.RequireAdult("marriage");
        var child = Make(1, 2, age: 10);
        var ex = Assert.Throws<InvalidOperationException>(() => child.RequireAdult("marriage"));
        Assert.Contains("marriage", ex.Message);
    }

    [Fact]
    public void Create_RejectsNoneId()
    {
        Assert.Throws<ArgumentException>(() => Make(1, 0));
        Assert.Throws<ArgumentException>(() => ActorTraits.Roll(1, ActorId.None, Balanced));
    }
}
