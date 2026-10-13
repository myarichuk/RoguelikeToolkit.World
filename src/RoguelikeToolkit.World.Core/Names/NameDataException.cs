using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Thrown when name tables fail startup validation (missing/duplicate/mismatched keys, bad YAML).</summary>
public sealed class NameDataException : Exception
{
    public IReadOnlyList<string> Problems { get; }
    public NameDataException(IEnumerable<string> problems)
        : base("Name data failed validation:\n- " + string.Join("\n- ", problems))
        => Problems = problems.ToArray();
}
