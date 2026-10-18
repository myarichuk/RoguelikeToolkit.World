using System.Text.Json;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.MapEditor.Engine;

/// <summary>
/// A plugin-agnostic read of a world's history for an editor panel: every declared scalar with its range, each polity's
/// values, the plugin event kinds seen and the plugin state keys held. The engine knows no plugin; a plugin that wants a
/// richer view decodes its own state-bag key on the host side.
/// </summary>
public static class HistorySummary
{
    /// <summary>The summary as JSON: <c>{scalars, polities, pluginEvents, pluginState}</c>.</summary>
    public static string ToJson(HistoryCatalog history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var reg = history.Scalars;
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteStartArray("scalars");
            for (int i = 0; i < reg.Count; i++)
            {
                var d = reg[i];
                w.WriteStartObject();
                w.WriteString("name", d.Name);
                w.WriteNumber("min", d.Min);
                w.WriteNumber("max", d.Max);
                w.WriteNumber("default", d.Default);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("polities");
            foreach (var p in history.Polities)
            {
                w.WriteStartObject();
                w.WriteNumber("id", p.Id);
                w.WriteBoolean("alive", p.Alive);
                w.WriteNumber("parent", p.Parent);
                w.WriteString("form", p.Form.ToString());
                w.WriteStartArray("values");
                for (int i = 0; i < reg.Count; i++) w.WriteNumberValue(p.GetScalar(i, reg));
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("pluginEvents");
            foreach (var kv in history.Events.Where(e => e.Kind == HistoryEventKind.Plugin)
                         .GroupBy(e => e.PluginKind, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WriteString("kind", kv.Key);
                w.WriteNumber("count", kv.Count());
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("pluginState");
            foreach (string key in history.PluginState.Keys) w.WriteStringValue(key);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
