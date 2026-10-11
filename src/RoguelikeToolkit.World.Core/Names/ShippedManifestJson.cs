using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core.Names;

/// <summary>
/// Reads/writes the flat <c>.shipped.json</c> manifest (relative path -> SHA-256 hex).
/// .NET uses System.Text.Json; netstandard2.1 (Unity) has no System.Text.Json in the box, so it parses with YamlDotNet's
/// node model (JSON is valid YAML; the node model needs no reflection, so it is IL2CPP/stripping safe) and writes with
/// a minimal escaping writer. Both sides produce and accept the same JSON object.
/// </summary>
internal static class ShippedManifestJson
{
    public static Dictionary<string, string>? Read(string json)
    {
#if NET
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
#else
        var stream = new YamlDotNet.RepresentationModel.YamlStream();
        stream.Load(new System.IO.StringReader(json));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlDotNet.RepresentationModel.YamlMappingNode root)
            return null;

        var result = new Dictionary<string, string>();
        foreach (var entry in root.Children)
        {
            if (entry.Key is YamlDotNet.RepresentationModel.YamlScalarNode key &&
                entry.Value is YamlDotNet.RepresentationModel.YamlScalarNode value &&
                key.Value != null && value.Value != null)
            {
                result[key.Value] = value.Value;
            }
        }
        return result;
#endif
    }

    public static string Write(Dictionary<string, string> manifest)
    {
#if NET
        return System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
#else
        var sb = new System.Text.StringBuilder("{");
        bool first = true;
        foreach (var kv in manifest)
        {
            sb.Append(first ? "\n  " : ",\n  ");
            first = false;
            AppendString(sb, kv.Key);
            sb.Append(": ");
            AppendString(sb, kv.Value);
        }
        sb.Append(first ? "}" : "\n}");
        return sb.ToString();
#endif
    }

#if !NET
    private static void AppendString(System.Text.StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
#endif
}
