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
    Native,
    Latin,
    Hebrew,
    Cyrillic,
}
