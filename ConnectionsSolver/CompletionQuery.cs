using System.Text.RegularExpressions;

namespace ConnectionsSolver;

/// <summary>
/// One ranked completion suggestion for a "with X, Y[, Z]" query.
/// <see cref="Group"/> is a full 4-set candidate (with labels); <see cref="PinnedKnownIndices"/>
/// are the known-words indices that the user pinned, so the printer can highlight which
/// slots were chosen by the solver vs. supplied by the user.
/// </summary>
public sealed record CompletionResult(
    CandidateGroup Group,
    int[] PinnedKnownIndices);

/// <summary>
/// Stateless "what goes with these words?" query used by the interactive prompt.
/// Enumerates every 4-set containing all pinned active indices, scores them with the
/// same Phase 2 (leftover-partition) + Phase 6 (label-overlap) pipeline used for the
/// regular anchor selection, and returns the top-K with labels.
/// </summary>
public static class CompletionQuery
{
    /// <summary>
    /// Parses the argument string after the "with" keyword into a list of active-coordinate
    /// indices. Returns null on any error (with <paramref name="error"/> populated).
    /// Supports 1-3 entries. Comma-separated for multi-word entries (e.g. "free love, hippie");
    /// otherwise whitespace-separated single-word entries.
    /// </summary>
    public static int[]? ResolvePinned(
        string args,
        IReadOnlyDictionary<string, int> activeWordToKnownIndex,
        int[] activeKnownIndices,
        out string? error)
    {
        error = null;
        var trimmed = args.Trim().ToLowerInvariant();
        if (trimmed.Length == 0)
        {
            error = "Need 1-3 words. Usage: with <word1>[, <word2>[, <word3>]]";
            return null;
        }

        string[] entries;
        if (trimmed.Contains(','))
        {
            entries = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => Regex.Replace(p.Trim(), @"\s+", " "))
                .Where(p => p.Length > 0)
                .ToArray();
        }
        else
        {
            entries = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        if (entries.Length < 1 || entries.Length > 3)
        {
            error = $"Need 1-3 entries; got {entries.Length}. " +
                    "(For a multi-word entry, separate entries with commas: 'with free love, hippie')";
            return null;
        }

        var knownToActive = new Dictionary<int, int>(activeKnownIndices.Length);
        for (int i = 0; i < activeKnownIndices.Length; i++) knownToActive[activeKnownIndices[i]] = i;

        var resolved = new List<int>(entries.Length);
        var seen = new HashSet<int>();
        foreach (var e in entries)
        {
            if (!activeWordToKnownIndex.TryGetValue(e, out var ki))
            {
                error = $"Unknown or inactive entry: '{e}'.";
                return null;
            }
            if (!knownToActive.TryGetValue(ki, out var ai))
            {
                error = $"'{e}' is no longer active.";
                return null;
            }
            if (!seen.Add(ai))
            {
                error = $"Duplicate entry: '{e}'.";
                return null;
            }
            resolved.Add(ai);
        }
        return resolved.ToArray();
    }

    /// <summary>
    /// Runs the completion query. The result list is already top-K ordered by combined score.
    /// </summary>
    public static List<CompletionResult> Run(
        int[] pinnedActiveIndices,
        int[] activeKnownIndices,
        string[] activeWords,
        float[][] activeUnits,
        string[] originalWords,
        LabelingContext labelCtx,
        HashSet<string> forbiddenSetKeys,
        int topK,
        int labelCount,
        double leftoverAlpha,
        int rerankTopN,
        double labelRerankBeta,
        int labelRerankTopN)
    {
        int activeN = activeWords.Length;
        if (pinnedActiveIndices.Length == 0 || pinnedActiveIndices.Length >= 4)
            return new List<CompletionResult>();
        if (activeN < 4) return new List<CompletionResult>();

        var activeSim = Similarity.BuildPairwiseMatrix(activeUnits);

        var candidates = EnumerateCompletions(pinnedActiveIndices, activeN, activeSim);
        if (candidates.Count == 0) return new List<CompletionResult>();

        // Filter forbidden 4-sets.
        var notForbidden = new List<(int[] Indices, double AvgSim)>(candidates.Count);
        foreach (var c in candidates)
        {
            var knownIdxs = new int[4];
            for (int i = 0; i < 4; i++) knownIdxs[i] = activeKnownIndices[c.Indices[i]];
            Array.Sort(knownIdxs);
            if (!forbiddenSetKeys.Contains(Solver.CanonicalKey(knownIdxs)))
                notForbidden.Add(c);
        }
        if (notForbidden.Count == 0) return new List<CompletionResult>();
        notForbidden.Sort((a, b) => b.AvgSim.CompareTo(a.AvgSim));

        // Reuse Phase 2 + Phase 6 reranking exactly as the main anchor pipeline does.
        var reranked = PartitionReranker.Rerank(
            notForbidden, activeN, activeSim, activeKnownIndices, forbiddenSetKeys,
            leftoverAlpha, rerankTopN);
        if (labelRerankBeta > 0 && labelRerankTopN > 0)
        {
            reranked = LabelOverlapReranker.Rerank(
                reranked, activeUnits, labelCtx, originalWords,
                labelRerankBeta, labelRerankTopN);
        }

        var pinnedKnown = new int[pinnedActiveIndices.Length];
        for (int i = 0; i < pinnedActiveIndices.Length; i++)
            pinnedKnown[i] = activeKnownIndices[pinnedActiveIndices[i]];
        Array.Sort(pinnedKnown);

        var results = new List<CompletionResult>(Math.Min(topK, reranked.Count));
        foreach (var r in reranked.Take(topK))
        {
            var groupWords = r.Indices.Select(i => activeWords[i]).ToArray();
            var vectors = r.Indices.Select(i => activeUnits[i]).ToList();
            var labels = GroupLabeler.Label(vectors, originalWords, labelCtx, labelCount)
                .Select(l => l.Word).ToArray();
            var knownIdxs = r.Indices.Select(i => activeKnownIndices[i]).ToArray();
            var cand = new CandidateGroup(knownIdxs, groupWords, r.AvgSim, labels)
            {
                LeftoverPartitionScore = r.LeftoverPartitionScore,
            };
            results.Add(new CompletionResult(cand, pinnedKnown));
        }
        return results;
    }

    /// <summary>
    /// Enumerates every 4-set containing all pinned active indices, with that subset's
    /// average pairwise cosine pre-computed. Pinned-pair similarity is hoisted out of the
    /// inner loop since it's constant across enumerations.
    /// </summary>
    private static List<(int[] Indices, double AvgSim)> EnumerateCompletions(
        int[] pinnedActive, int activeN, float[,] sim)
    {
        int need = 4 - pinnedActive.Length;
        var pinnedSet = new HashSet<int>(pinnedActive);
        var pool = new int[activeN - pinnedActive.Length];
        int pi = 0;
        for (int i = 0; i < activeN; i++)
            if (!pinnedSet.Contains(i)) pool[pi++] = i;

        double pinnedPairSum = 0;
        for (int a = 0; a < pinnedActive.Length; a++)
            for (int b = a + 1; b < pinnedActive.Length; b++)
                pinnedPairSum += sim[pinnedActive[a], pinnedActive[b]];

        var results = new List<(int[] Indices, double AvgSim)>();
        var picks = new int[need];

        void Recurse(int depth, int start)
        {
            if (depth == need)
            {
                double total = pinnedPairSum;
                for (int x = 0; x < pinnedActive.Length; x++)
                    for (int y = 0; y < need; y++)
                        total += sim[pinnedActive[x], picks[y]];
                for (int x = 0; x < need; x++)
                    for (int y = x + 1; y < need; y++)
                        total += sim[picks[x], picks[y]];
                double avg = total / 6.0;
                var four = new int[4];
                int k = 0;
                foreach (var p in pinnedActive) four[k++] = p;
                foreach (var p in picks) four[k++] = p;
                Array.Sort(four);
                results.Add((four, avg));
                return;
            }
            for (int i = start; i <= pool.Length - (need - depth); i++)
            {
                picks[depth] = pool[i];
                Recurse(depth + 1, i + 1);
            }
        }
        Recurse(0, 0);
        return results;
    }
}
