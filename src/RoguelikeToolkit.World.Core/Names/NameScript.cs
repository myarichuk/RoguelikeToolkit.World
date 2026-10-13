namespace RoguelikeToolkit.World.Core.Names;

/// <summary>
/// Which script a generated name renders in. <see cref="Native"/> and
/// <see cref="Latin"/> use the authored (or transliterated) table forms;
/// <see cref="Hebrew"/> and <see cref="Cyrillic"/> machine-transliterate any
/// Latin form via <see cref="Transliterator"/>, while morphemes already in the
/// target script (or carrying an explicit field) pass through untouched.
/// Glosses stay in English either way.
/// </summary>
public enum NameScript
{
    /// <summary>The table's authored forms as written (Cyrillic or Hebrew where the table is in those scripts).</summary>
    Native,
    /// <summary>Latin letters: the authored Latin transliteration where provided, else the form itself.</summary>
    Latin,
    /// <summary>Hebrew script, machine-transliterated from Latin forms where needed.</summary>
    Hebrew,
    /// <summary>Cyrillic script, machine-transliterated from Latin forms where needed.</summary>
    Cyrillic,
}
