using System.Text;

namespace RoguelikeToolkit.World.Core.Primitives;

/// <summary>
/// A namespace-independent identity for a type, used in persisted file headers. Hashing
/// <see cref="Type.FullName"/> made every namespace move a file-format break; this hashes only the
/// declaring-type chain, the simple name and generic arguments, so folders can be reorganised freely.
/// Renaming a persisted type still changes its id and must be accompanied by a file version bump.
/// </summary>
internal static class StableTypeId
{
    /// <summary>FNV-1a 64 over <see cref="Name"/>.</summary>
    public static ulong Hash(Type type)
    {
        ulong hash = 14695981039346656037ul;
        string name = Name(type);
        for (int i = 0; i < name.Length; i++)
        {
            hash ^= (byte)name[i];
            hash *= 1099511628211ul;
        }
        return hash;
    }

    /// <summary>The type's name without namespaces: <c>Outer+Inner</c>, <c>Box`1[Vec]</c>.</summary>
    public static string Name(Type type)
    {
        var sb = new StringBuilder();
        Append(sb, type);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, Type type)
    {
        if (type.DeclaringType is { } outer && !type.IsGenericParameter)
        {
            Append(sb, outer);
            sb.Append('+');
        }
        string simple = type.Name;
        sb.Append(simple);
        if (type.IsConstructedGenericType)
        {
            sb.Append('[');
            var args = type.GenericTypeArguments;
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(',');
                Append(sb, args[i]);
            }
            sb.Append(']');
        }
    }
}
