namespace RoguelikeToolkit.World.Core.History;

/// <summary>What an <see cref="EventParticipant.EntityId"/> indexes, read from its role (see <see cref="ParticipantRoles"/>).</summary>
public enum ParticipantEntity : byte
{
    /// <summary>A polity id.</summary>
    Polity = 0,
    /// <summary>A person id.</summary>
    Person = 1,
    /// <summary>A settlement index.</summary>
    Settlement = 2,
}
