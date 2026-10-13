namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Role values for <see cref="NamePart.Role"/>.</summary>
public static class NamePartRoles
{
    /// <summary>The personal name.</summary>
    public const string Given = "given";
    /// <summary>Name derived from the father's name ("Bran"+"son", "ben David").</summary>
    public const string Patronymic = "patronymic";
    /// <summary>Name derived from the mother's name; used instead of <see cref="Patronymic"/> for matrilineal lines.</summary>
    public const string Matronymic = "matronymic";
    /// <summary>Family or house name of a human (also a borrowed human family name on half-bloods).</summary>
    public const string Family = "family";
    /// <summary>Clan or warband name of a non-human folk.</summary>
    public const string Clan = "clan";
    /// <summary>Added by-name or deed-name after the rest of the name.</summary>
    public const string Epithet = "epithet";
    /// <summary>The qualifying word of a place name (the "Clear" of Clearford).</summary>
    public const string Descriptor = "descriptor";
    /// <summary>The terrain or settlement noun of a place name (the "ford" of Clearford).</summary>
    public const string Geography = "geography";
}
