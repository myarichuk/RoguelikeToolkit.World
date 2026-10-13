namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Thrown when name tables fail startup validation (missing/duplicate/mismatched keys, bad YAML).</summary>
public sealed class NameDataException : Exception
{
    /// <summary>Every validation problem found, one message per problem, each prefixed with the offending file.</summary>
    public IReadOnlyList<string> Problems { get; }
    /// <summary>Creates the exception from the full list of load problems; the message lists them all.</summary>
    public NameDataException(IEnumerable<string> problems)
        : base("Name data failed validation:\n- " + string.Join("\n- ", problems))
        => Problems = problems.ToArray();
}
