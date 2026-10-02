using System;
using System.Collections.Generic;
using System.Linq;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Validates declared field-layer contracts (attribute or
/// <see cref="IDeclaredStage"/>; <c>ReadsOptional</c>
/// is skipped). Stages without declarations are skipped,
/// so legacy/custom stages without contracts keep working.
/// Rule: two stages may write the same field layer only as an ordered
/// read-modify-write chain — every writer after the first must also read the
/// type. A blind second writer (writes without reading) is a collision.
/// </summary>
public static class StageContractValidator
{
    public sealed record Conflict(string Message);

    public static IReadOnlyList<Conflict> FindConflicts(IReadOnlyList<IWorldGeneratorStage> stages)
    {
        var conflicts = new List<Conflict>();
        var ordered = stages
            .Where(StageMetadata.HasDeclaration)
            .Select(s => (Stage: s, Order: StageMetadata.GetOrder(s)))
            .OrderBy(x => x.Order)
            .ToList();

        var writersByType = new Dictionary<Type, List<string>>();

        foreach (var (stage, order) in ordered)
        {
            string name = StageMetadata.DisplayName(stage);
            var reads = new HashSet<Type>(StageMetadata.GetReads(stage));
            var writes = StageMetadata.GetWrites(stage);

            foreach (var w in writes)
            {
                if (!writersByType.TryGetValue(w, out var prior))
                {
                    prior = new List<string>();
                    writersByType[w] = prior;
                }

                // First writer is fine. Later writers must read the type (refinement).
                if (prior.Count > 0 && !reads.Contains(w))
                {
                    conflicts.Add(new Conflict(
                        $"Stage '{name}' blind-writes '{w.Name}' already written by [{string.Join(", ", prior)}] " +
                        $"without declaring Reads. Declare Reads to join the refinement chain or use a different layer."));
                }

                prior.Add(name);
            }
        }

        // Ordering check: a stage reading T should have an earlier writer of T.
        // Only applies when at least one stage writes T; pure inputs (topology) need no writer.
        var firstWriterOrder = new Dictionary<Type, int>();
        foreach (var (stage, order) in ordered)
        {
            foreach (var w in StageMetadata.GetWrites(stage))
            {
                if (!firstWriterOrder.ContainsKey(w))
                    firstWriterOrder[w] = order;
            }
        }

        foreach (var (stage, order) in ordered)
        {
            // Required reads only: ReadsOptional layers may be absent or
            // written later; stages using them degrade gracefully.
            foreach (var r in StageMetadata.GetReads(stage))
            {
                if (firstWriterOrder.TryGetValue(r, out int firstWrite) && order < firstWrite)
                {
                    conflicts.Add(new Conflict(
                        $"Stage '{StageMetadata.DisplayName(stage)}' (Order {order}) reads '{r.Name}' " +
                        $"but the first writer runs at Order {firstWrite}. Fix Order values."));
                }
            }
        }

        return conflicts;
    }

    public static void ThrowOnConflicts(IReadOnlyList<IWorldGeneratorStage> stages)
    {
        var conflicts = FindConflicts(stages);
        if (conflicts.Count > 0)
            throw new InvalidOperationException("Pipeline stage contract conflicts: " +
                string.Join(" | ", conflicts.Select(c => c.Message)));
    }
}
