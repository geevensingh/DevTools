namespace ConnectionsSolver;

/// <summary>
/// Finds dense clusters of 5+ active words that all sit in the same tight neighborhood.
/// A cluster surfaces a "red herring" risk: a tight 4-anchor (e.g. bassoon/fiddle/flute/piano)
/// extended by 1-2 distractors that look semantically similar (e.g. allegro, forte) — the
/// user must pick the correct 4 from the bigger pile.
///
/// Approach: for each of the top-K disjoint candidate 4-anchors, find any extra active word
/// whose mean similarity to the anchor exceeds <c>anchorTightness * extensionRatio</c>
/// (floored at a minimum absolute similarity). The anchor + those extras is the cluster.
/// Clusters smaller than <paramref name="minClusterSize"/> are dropped, and exact duplicates
/// are deduplicated across anchors.
/// </summary>
public static class ClusterDetector
{
    public static List<int[]> FindDenseClusters(
        int activeN,
        float[,] sim,
        List<(int[] Indices, double AvgSim)> rankedAnchors,
        int maxAnchorsToConsider = 5,
        double extensionRatio = 0.75,
        double minSimFloor = 0.30,
        int minClusterSize = 5,
        int maxClusterSize = 8)
    {
        if (activeN < minClusterSize) return new List<int[]>();

        var seen = new HashSet<string>();
        var clusters = new List<(int[] Members, double Score)>();
        int considered = 0;

        foreach (var anchor in rankedAnchors)
        {
            if (considered >= maxAnchorsToConsider) break;
            considered++;
            if (anchor.Indices.Length != 4) continue;

            double threshold = Math.Max(anchor.AvgSim * extensionRatio, minSimFloor);
            var anchorSet = new HashSet<int>(anchor.Indices);
            var extensions = new List<(int Idx, double MeanSim)>();
            for (int w = 0; w < activeN; w++)
            {
                if (anchorSet.Contains(w)) continue;
                double s = 0;
                foreach (var ai in anchor.Indices) s += sim[w, ai];
                double meanSim = s / 4.0;
                if (meanSim >= threshold) extensions.Add((w, meanSim));
            }
            if (extensions.Count == 0) continue;

            extensions.Sort((a, b) => b.MeanSim.CompareTo(a.MeanSim));
            int extraCap = Math.Max(0, maxClusterSize - 4);
            int extraCount = Math.Min(extensions.Count, extraCap);
            var members = new List<int>(anchor.Indices);
            for (int i = 0; i < extraCount; i++) members.Add(extensions[i].Idx);
            if (members.Count < minClusterSize) continue;

            members.Sort();
            var key = string.Join(",", members);
            if (!seen.Add(key)) continue;
            clusters.Add((members.ToArray(), anchor.AvgSim));
        }

        clusters.Sort((a, b) => b.Score.CompareTo(a.Score));
        return clusters.Select(c => c.Members).ToList();
    }
}
