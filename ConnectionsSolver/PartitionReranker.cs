namespace ConnectionsSolver;

/// <summary>
/// Reorders a candidate list by combined score (tightness + alpha * leftover-partition-score).
/// The idea: a candidate 4-set is "more likely correct" if removing it leaves the remaining
/// active words in a configuration that ALSO admits a tight partition. This penalises
/// candidates that "steal" words from other groups (e.g. lumping PIANO with VIOLIN/GUITAR/DRUMS
/// when PIANO actually belongs to a music-directions group).
///
/// Only the top <paramref name="topN"/> candidates are reranked (computing a partition of the
/// leftover 12 words for every one of ~1800 4-subsets is wasteful). Below-top candidates keep
/// their original order; their combined score is treated as just their tightness.
///
/// Reranking fires whenever the leftover has at least 4 words. When the leftover size is not a
/// multiple of 4, the "loneliest" word(s) (lowest mean similarity to other leftovers) are
/// dropped so the remainder partitions evenly. Dropped words still count as active in
/// downstream picking; they are simply ignored for this heuristic score.
/// </summary>
public static class PartitionReranker
{
    public sealed record Reranked(
        int[] Indices,
        double AvgSim,
        double? LeftoverPartitionScore,
        double CombinedScore);

    public static List<Reranked> Rerank(
        List<(int[] Indices, double AvgSim)> candidates,
        int activeN,
        float[,] activeSim,
        int[] activeKnownIndices,
        HashSet<string> forbiddenSetKeys,
        double alpha,
        int topN)
    {
        var output = new List<Reranked>(candidates.Count);
        int leftoverN = activeN - 4;
        int groupCount = leftoverN / 4;
        bool canPartition = groupCount >= 1;
        if (!canPartition || candidates.Count == 0)
        {
            foreach (var c in candidates)
                output.Add(new Reranked(c.Indices, c.AvgSim, null, c.AvgSim));
            return output;
        }

        int N = Math.Min(topN, candidates.Count);
        int usedN = groupCount * 4;
        int dropCount = leftoverN - usedN;
        var topScored = new Reranked[N];

        for (int idx = 0; idx < N; idx++)
        {
            var c = candidates[idx];
            var inGroup = new HashSet<int>(c.Indices);
            var leftover = new int[leftoverN];
            int k = 0;
            for (int a = 0; a < activeN; a++)
                if (!inGroup.Contains(a)) leftover[k++] = a;

            // When leftoverN is not a multiple of 4, drop the "loneliest" words
            // (lowest mean similarity to other leftover words) so the remainder
            // partitions evenly. This is a heuristic — the dropped words are
            // simply not scored, not excluded from candidate consideration.
            int[] usedLeftover;
            if (dropCount == 0)
            {
                usedLeftover = leftover;
            }
            else
            {
                var meanSim = new double[leftoverN];
                for (int p = 0; p < leftoverN; p++)
                {
                    double s = 0;
                    for (int q = 0; q < leftoverN; q++)
                        if (q != p) s += activeSim[leftover[p], leftover[q]];
                    meanSim[p] = s / (leftoverN - 1);
                }
                var order = Enumerable.Range(0, leftoverN).OrderByDescending(i => meanSim[i]).Take(usedN).ToArray();
                Array.Sort(order);
                usedLeftover = new int[usedN];
                for (int j = 0; j < usedN; j++) usedLeftover[j] = leftover[order[j]];
            }

            var subSim = new float[usedN, usedN];
            for (int p = 0; p < usedN; p++)
                for (int q = 0; q < usedN; q++)
                    subSim[p, q] = activeSim[usedLeftover[p], usedLeftover[q]];

            Func<int[], bool>? forbidCheck = null;
            if (forbiddenSetKeys.Count > 0)
            {
                forbidCheck = subIdxs =>
                {
                    if (subIdxs.Length != 4) return false;
                    var knownIdxs = new int[4];
                    for (int j = 0; j < 4; j++) knownIdxs[j] = activeKnownIndices[usedLeftover[subIdxs[j]]];
                    Array.Sort(knownIdxs);
                    return forbiddenSetKeys.Contains(Solver.CanonicalKey(knownIdxs));
                };
            }

            var parts = PartitionSearch.EnumerateTopK(subSim, 1, usedN, groupCount, 4, forbidCheck);
            double leftoverScore = parts.Count > 0 ? parts[0].PerGroupAvgSim.Average() : 0;
            double combined = c.AvgSim + alpha * leftoverScore;
            topScored[idx] = new Reranked(c.Indices, c.AvgSim, leftoverScore, combined);
        }

        Array.Sort(topScored, (a, b) => b.CombinedScore.CompareTo(a.CombinedScore));
        foreach (var s in topScored) output.Add(s);
        for (int i = N; i < candidates.Count; i++)
        {
            var c = candidates[i];
            output.Add(new Reranked(c.Indices, c.AvgSim, null, c.AvgSim));
        }
        return output;
    }
}
