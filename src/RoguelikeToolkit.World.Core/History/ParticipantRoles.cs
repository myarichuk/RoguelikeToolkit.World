namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Event participant roles name the kind of entity they refer to by prefix: <c>person:commander</c> is a person,
/// <c>settlement:besieged</c> a settlement, and a bare role (<c>attacker</c>) a polity, as every legacy role is.
/// </summary>
public static class ParticipantRoles
{
    /// <summary>Prefix of roles played by a person (<see cref="HistoryCatalog.Persons"/>).</summary>
    public const string PersonPrefix = "person:";

    /// <summary>Prefix of roles played by a settlement (<see cref="HistoryCatalog.Settlements"/>).</summary>
    public const string SettlementPrefix = "settlement:";

    /// <summary>The kind of entity a participant with role <paramref name="role"/> refers to.</summary>
    public static ParticipantEntity EntityOf(string role)
    {
        if (role == null) return ParticipantEntity.Polity;
        if (role.StartsWith(PersonPrefix, StringComparison.Ordinal)) return ParticipantEntity.Person;
        if (role.StartsWith(SettlementPrefix, StringComparison.Ordinal)) return ParticipantEntity.Settlement;
        return ParticipantEntity.Polity;
    }
}
