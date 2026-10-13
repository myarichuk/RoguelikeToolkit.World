namespace RoguelikeToolkit.World.Core.Generation;

/// <summary>A stage whose random seed can be set from outside; hosts re-seed every such stage in a pipeline generically.</summary>
public interface ISeededStage
{
    /// <summary>Seed for the stage's noise and random draws; the same seed and inputs give the same output.</summary>
    int Seed { get; set; }
}
