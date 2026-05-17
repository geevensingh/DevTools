namespace ConnectionsSolver;

public sealed record PartitionResult(int[][] Groups, double TotalScore, double[] PerGroupAvgSim);

public static class PartitionSearch
{
    /// <summary>
    /// Enumerates all partitions of <paramref name="n"/> items into <paramref name="groups"/>
    /// unordered groups each of size <paramref name="groupSize"/>, scoring by total within-group
    /// cosine similarity from <paramref name="sim"/>. Returns the top <paramref name="topK"/>
    /// partitions, highest score first.
    ///
    /// If <paramref name="isGroupForbidden"/> is supplied it is invoked once per completed group
    /// during enumeration; returning true prunes that whole branch. The callback receives the
    /// group's indices sorted ascending.
    ///
    /// For NYT Connections, defaults are 16 items / 4 groups / 4 each → ~2.6M partitions, sub-second.
    /// </summary>
    public static List<PartitionResult> EnumerateTopK(
        float[,] sim,
        int topK,
        int n = 16,
        int groups = 4,
        int groupSize = 4,
        Func<int[], bool>? isGroupForbidden = null)
    {
        if (n != groups * groupSize)
            throw new ArgumentException($"n ({n}) must equal groups * groupSize ({groups * groupSize})");
        if (topK <= 0) return new List<PartitionResult>();

        var heap = new PriorityQueue<int[], double>();
        var groupOf = new int[n];
        var groupCounts = new int[groups];
        var scratch = new int[groupSize];

        void TryAdd(double score)
        {
            if (heap.Count < topK)
            {
                heap.Enqueue((int[])groupOf.Clone(), score);
            }
            else if (heap.TryPeek(out _, out var minScore) && score > minScore)
            {
                heap.Dequeue();
                heap.Enqueue((int[])groupOf.Clone(), score);
            }
        }

        bool IsCompletedGroupForbidden(int g, int idxJustAdded)
        {
            if (isGroupForbidden == null) return false;
            int k = 0;
            for (int j = 0; j <= idxJustAdded; j++)
                if (groupOf[j] == g) scratch[k++] = j;
            Array.Sort(scratch, 0, k);
            return isGroupForbidden(scratch[..k]);
        }

        void Recurse(int idx, int maxGroupUsed, double currentScore)
        {
            if (idx == n)
            {
                TryAdd(currentScore);
                return;
            }

            // Canonical form: only allow starting group g if g <= maxGroupUsed + 1.
            // Eliminates duplicates produced by reordering groups.
            int upperBound = System.Math.Min(maxGroupUsed + 1, groups - 1);
            for (int g = 0; g <= upperBound; g++)
            {
                if (groupCounts[g] >= groupSize) continue;

                double inc = 0;
                for (int j = 0; j < idx; j++)
                    if (groupOf[j] == g)
                        inc += sim[idx, j];

                groupOf[idx] = g;
                groupCounts[g]++;
                bool prune = groupCounts[g] == groupSize && IsCompletedGroupForbidden(g, idx);
                if (!prune)
                    Recurse(idx + 1, System.Math.Max(maxGroupUsed, g), currentScore + inc);
                groupCounts[g]--;
            }
        }

        Recurse(0, -1, 0.0);

        var drained = new List<(int[] gOf, double score)>(heap.Count);
        while (heap.TryDequeue(out var item, out var score))
            drained.Add((item, score));
        drained.Sort((a, b) => b.score.CompareTo(a.score));

        return drained.Select(d => BuildResult(d.gOf, d.score, sim, groups, groupSize)).ToList();
    }

    private static PartitionResult BuildResult(int[] groupOf, double totalScore, float[,] sim, int groups, int groupSize)
    {
        var groupsArr = new int[groups][];
        for (int k = 0; k < groups; k++) groupsArr[k] = new int[groupSize];
        var counts = new int[groups];
        for (int i = 0; i < groupOf.Length; i++)
        {
            int g = groupOf[i];
            groupsArr[g][counts[g]++] = i;
        }

        int pairs = groupSize * (groupSize - 1) / 2;
        var perGroup = new double[groups];
        for (int k = 0; k < groups; k++)
        {
            double s = 0;
            for (int i = 0; i < groupSize; i++)
                for (int j = i + 1; j < groupSize; j++)
                    s += sim[groupsArr[k][i], groupsArr[k][j]];
            perGroup[k] = s / pairs;
        }
        return new PartitionResult(groupsArr, totalScore, perGroup);
    }
}
