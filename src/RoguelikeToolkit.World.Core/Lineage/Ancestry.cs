namespace RoguelikeToolkit.World.Core.Lineage;

/// <summary>
/// An actor's lineage: folk shares sorted steeply first. The top two or three shares
/// define the implied status (see <see cref="ResolveImpliedStatus"/>); the full list is
/// kept so later generations can drift or blend it further.
/// </summary>
public sealed class Ancestry
{
    /// <summary>Marker returned by <see cref="ResolveImpliedStatus"/> when no share dominates.</summary>
    public const string Mixed = "mixed";

    /// <summary>Default dominant-share threshold: a share at or above it sets the implied status.</summary>
    public const double DefaultDominantThreshold = 0.75;

    /// <summary>Shares in descending order (ties keep declaration order).</summary>
    public IReadOnlyList<AncestryShare> Shares { get; }

    private Ancestry(AncestryShare[] shares) => Shares = shares;

    /// <summary>
    /// Builds a lineage from folk shares. Shares are sorted steeply first.
    /// </summary>
    /// <param name="shares">At least one share; each folk non-blank, each share in (0, 1], totalling at most 1 (plus rounding).</param>
    /// <exception cref="ArgumentNullException"><paramref name="shares"/> is null.</exception>
    /// <exception cref="ArgumentException">No shares, a blank folk, or a total above 1.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A share outside (0, 1].</exception>
    public static Ancestry From(params AncestryShare[] shares)
    {
        if (shares is null) throw new ArgumentNullException(nameof(shares));
        if (shares.Length == 0) throw new ArgumentException("Ancestry needs at least one share.", nameof(shares));
        double total = 0;
        foreach (var s in shares)
        {
            if (string.IsNullOrWhiteSpace(s.Folk)) throw new ArgumentException("Every ancestry share needs a folk.", nameof(shares));
            if (double.IsNaN(s.Share) || s.Share <= 0 || s.Share > 1)
                throw new ArgumentOutOfRangeException(nameof(shares), $"Share {s.Share} for '{s.Folk}' is outside (0, 1].");
            total += s.Share;
        }
        if (total > 1.0 + 1e-9) throw new ArgumentException($"Ancestry shares total {total}, above 1.", nameof(shares));
        // OrderByDescending is a stable sort: ties keep declaration order, so a
        // declared-first folk wins exact ties deterministically.
        return new Ancestry(shares.OrderByDescending(s => s.Share).ToArray());
    }

    /// <summary>The steepest share's folk.</summary>
    public string Dominant => Shares[0].Folk;

    /// <summary>
    /// Resolves the implied status: a lone share, or a share at/above
    /// <paramref name="dominantThreshold"/>, yields that folk; otherwise
    /// <see cref="Mixed"/>. Takes the threshold as a plain number (the owning
    /// culture's hypodescent threshold) so this rank-1 slice never reads Peoples data.
    /// A 0.5/0.5 ancestry is mixed under the default threshold.
    /// </summary>
    /// <param name="dominantThreshold">Share at or above which one folk dominates, in [0.5, 1].</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dominantThreshold"/> is outside [0.5, 1].</exception>
    public string ResolveImpliedStatus(double dominantThreshold = DefaultDominantThreshold)
    {
        if (double.IsNaN(dominantThreshold) || dominantThreshold < 0.5 || dominantThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(dominantThreshold), "Dominant threshold must be in [0.5, 1].");
        if (Shares.Count == 1 || Shares[0].Share >= dominantThreshold) return Shares[0].Folk;
        return Mixed;
    }
}
