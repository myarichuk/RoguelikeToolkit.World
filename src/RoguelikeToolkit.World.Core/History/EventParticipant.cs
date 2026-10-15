namespace RoguelikeToolkit.World.Core.History;

/// <summary>An entity taking part in an event, with the role it played (e.g. attacker, defender, winner).</summary>
/// <param name="EntityId">Polity id today; entity ids of other kinds join in later milestones.</param>
/// <param name="Role">Interned role id from the catalog's <see cref="StringTable"/>.</param>
public readonly record struct EventParticipant(int EntityId, int Role);
