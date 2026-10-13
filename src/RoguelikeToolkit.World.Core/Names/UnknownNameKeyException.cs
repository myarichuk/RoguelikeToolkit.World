namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Thrown when a race/culture key has no table. Carries the available keys plus a closest match.</summary>
public sealed class UnknownNameKeyException : Exception
{
    /// <summary>The key that was looked up.</summary>
    public string Key { get; }
    /// <summary>What kind of key it was (<c>race</c>, <c>culture</c> or <c>table</c>).</summary>
    public string Kind { get; }
    /// <summary>All valid keys and aliases, sorted case-insensitively.</summary>
    public IReadOnlyList<string> Available { get; }
    /// <summary>The closest available key by edit distance, or null when nothing is close enough.</summary>
    public string? Suggestion { get; }

    /// <summary>Creates the exception; the message lists the available keys and, when one is close, a suggestion.</summary>
    public UnknownNameKeyException(string kind, string key, IEnumerable<string> available)
        : base(BuildMessage(kind, key, available, out var suggestion, out var avail))
    {
        Key = key;
        Kind = kind;
        Available = avail;
        Suggestion = suggestion;
    }

    private static string BuildMessage(string kind, string key, IEnumerable<string> available,
        out string? suggestion, out string[] avail)
    {
        avail = available.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray();
        suggestion = ClosestMatch(key, avail);
        var msg = $"Unknown {kind} '{key}'. Available {kind}s: {string.Join(", ", avail)}.";
        if (suggestion is not null) msg += $" Did you mean '{suggestion}'?";
        return msg;
    }

    internal static string? ClosestMatch(string key, string[] candidates)
    {
        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var c in candidates)
        {
            int d = Levenshtein(key.ToLowerInvariant(), c.ToLowerInvariant());
            if (d < bestDist) { bestDist = d; best = c; }
        }
        int threshold = Math.Max(2, key.Length / 3);
        return bestDist <= threshold ? best : null;
    }

    private static int Levenshtein(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return dp[a.Length, b.Length];
    }
}
