using System.Text;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// A parsed template: literal text with <c>{slot|mod|mod}</c> slots and <c>{#symbol}</c> expansions. A trailing <c>?</c> on a
/// symbol (<c>{#battle.cry?}</c>) makes it optional: when no rule of it can be rendered it yields nothing instead of failing the
/// rule. Modifiers apply left to right; see <see cref="ChronicleText.Modifiers"/>.
/// </summary>
internal sealed class ChronicleTemplate
{
    /// <summary>One piece of a template.</summary>
    internal readonly struct Part
    {
        /// <summary>Builds a part.</summary>
        public Part(PartKind kind, string text, string[] modifiers, bool optional)
        {
            Kind = kind;
            Text = text;
            Modifiers = modifiers;
            Optional = optional;
        }

        /// <summary>What the part is.</summary>
        public PartKind Kind { get; }
        /// <summary>The literal text, the slot name or the symbol name.</summary>
        public string Text { get; }
        /// <summary>Modifiers of a slot or symbol, in order.</summary>
        public string[] Modifiers { get; }
        /// <summary>True for an optional symbol.</summary>
        public bool Optional { get; }
    }

    /// <summary>Kinds of template parts.</summary>
    internal enum PartKind : byte
    {
        /// <summary>Copied as is.</summary>
        Literal = 0,
        /// <summary>A fact or lexicon slot.</summary>
        Slot = 1,
        /// <summary>A nested symbol expansion.</summary>
        Symbol = 2,
    }

    private ChronicleTemplate(string source, Part[] parts)
    {
        Source = source;
        Parts = parts;
    }

    /// <summary>The template text.</summary>
    public string Source { get; }
    /// <summary>The parsed parts.</summary>
    public Part[] Parts { get; }

    /// <summary>Parses <paramref name="text"/>; false (with a reason) on an unbalanced brace, an empty slot or a bad name.</summary>
    public static bool TryParse(string? text, out ChronicleTemplate? template, out string error)
    {
        template = null;
        error = string.Empty;
        string s = text ?? string.Empty;
        var parts = new List<Part>();
        var literal = new StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '}') { error = $"stray '}}' at {i}"; return false; }
            if (c != '{')
            {
                literal.Append(c);
                i++;
                continue;
            }
            int close = s.IndexOf('}', i + 1);
            int reopen = s.IndexOf('{', i + 1);
            if (close < 0 || (reopen >= 0 && reopen < close)) { error = $"unclosed '{{' at {i}"; return false; }
            if (literal.Length > 0)
            {
                parts.Add(new Part(PartKind.Literal, literal.ToString(), Array.Empty<string>(), false));
                literal.Clear();
            }
            string body = s.Substring(i + 1, close - i - 1).Trim();
            bool symbol = body.StartsWith("#", StringComparison.Ordinal);
            if (symbol) body = body.Substring(1);
            string[] pieces = body.Split('|');
            string name = pieces[0].Trim();
            bool optional = false;
            if (name.EndsWith("?", StringComparison.Ordinal))
            {
                if (!symbol) { error = $"only symbols can be optional: '{{{body}}}'"; return false; }
                optional = true;
                name = name.Substring(0, name.Length - 1);
            }
            if (!ChroniclePredicate.IsName(name)) { error = $"bad name in '{{{body}}}'"; return false; }
            var mods = new string[pieces.Length - 1];
            for (int m = 1; m < pieces.Length; m++)
            {
                mods[m - 1] = pieces[m].Trim();
                if (!ChronicleText.IsModifier(mods[m - 1])) { error = $"unknown modifier '{mods[m - 1]}' in '{{{body}}}'"; return false; }
            }
            parts.Add(new Part(symbol ? PartKind.Symbol : PartKind.Slot, name, mods, optional));
            i = close + 1;
        }
        if (literal.Length > 0) parts.Add(new Part(PartKind.Literal, literal.ToString(), Array.Empty<string>(), false));
        template = new ChronicleTemplate(s, parts.ToArray());
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Source;
}
