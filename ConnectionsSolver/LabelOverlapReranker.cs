namespace ConnectionsSolver;

/// <summary>
/// Phase 6: reorders the top-N candidates by adding a "label-overlap" signal to the
/// existing partition-reranked score. The signal is the mean cosine similarity from
/// the candidate's centroid to its top-K nearest label-vocabulary words.
///
/// Idea: a candidate 4-set is "more likely a real Connections group" if a single
/// dominant theme word from general English sits very close to its centroid - even
/// when pairwise cosine between the 4 words is moderate (e.g. polysemous words like
/// HERO/SUB where the dominant GloVe sense isn't "sandwich"). Conversely, a group
/// with high pairwise cosine but no single dominant centroid-label match is more
/// likely a "noise cluster" of generically similar words.
///
/// Final score: <c>candidate.CombinedScore + beta * mean(top-K centroid→label sims)</c>.
/// CombinedScore is the post-Phase-2 score (AvgSim + leftoverAlpha * leftoverScore);
/// adding beta * labelStrength composes the two signals rather than replacing one.
///
/// Only the top <paramref name="topN"/> candidates are rescored (computing N vocab
/// dot-products per candidate is the bottleneck). Below-top candidates keep their
/// original order.
/// </summary>
public static class LabelOverlapReranker
{
    /// <summary>How many vocab entries (from the front of <see cref="LabelingContext"/>)
    /// are considered when computing a candidate's label strength.</summary>
    public const int DefaultLabelVocabSlice = 5000;

    /// <summary>How many top centroid→label sims are averaged into the strength signal.</summary>
    public const int DefaultTopKLabels = 3;

    public static List<PartitionReranker.Reranked> Rerank(
        List<PartitionReranker.Reranked> candidates,
        float[][] activeUnits,
        LabelingContext labelCtx,
        IEnumerable<string> excludeWords,
        double beta,
        int topN,
        int labelVocabSlice = DefaultLabelVocabSlice,
        int topKLabels = DefaultTopKLabels)
    {
        if (candidates.Count == 0 || beta <= 0 || topN <= 0) return candidates;

        int sliceCount = Math.Min(labelVocabSlice, labelCtx.Words.Length);
        int N = Math.Min(topN, candidates.Count);
        var excludeSet = GroupLabeler.BuildExcludeSet(excludeWords);
        var skipMask = new bool[sliceCount];
        for (int v = 0; v < sliceCount; v++)
            if (excludeSet.Contains(labelCtx.Words[v])) skipMask[v] = true;

        int dim = activeUnits.Length > 0 ? activeUnits[0].Length : 0;
        var scored = new (PartitionReranker.Reranked Cand, double Final)[N];
        var centroid = new float[dim];
        var minHeap = new PriorityQueue<int, float>(topKLabels);

        for (int idx = 0; idx < N; idx++)
        {
            var c = candidates[idx];
            // Build unit centroid of the 4 active vectors.
            Array.Clear(centroid, 0, dim);
            for (int j = 0; j < c.Indices.Length; j++)
            {
                var v = activeUnits[c.Indices[j]];
                for (int d = 0; d < dim; d++) centroid[d] += v[d];
            }
            double mag = 0;
            for (int d = 0; d < dim; d++) mag += centroid[d] * centroid[d];
            mag = Math.Sqrt(mag);
            if (mag < 1e-9)
            {
                scored[idx] = (c, c.CombinedScore);
                continue;
            }
            float invMag = (float)(1.0 / mag);
            for (int d = 0; d < dim; d++) centroid[d] *= invMag;

            // Find the top-K vocab sims to the centroid, skipping excluded entries.
            minHeap.Clear();
            for (int v = 0; v < sliceCount; v++)
            {
                if (skipMask[v]) continue;
                var u = labelCtx.UnitVectors[v];
                double s = 0;
                for (int d = 0; d < dim; d++) s += centroid[d] * u[d];
                float sf = (float)s;
                if (minHeap.Count < topKLabels)
                {
                    minHeap.Enqueue(v, sf);
                }
                else if (minHeap.TryPeek(out _, out var minScore) && sf > minScore)
                {
                    minHeap.Dequeue();
                    minHeap.Enqueue(v, sf);
                }
            }

            double sum = 0;
            int count = 0;
            while (minHeap.TryDequeue(out _, out var s)) { sum += s; count++; }
            double labelStrength = count > 0 ? sum / count : 0;

            double final = c.CombinedScore + beta * labelStrength;
            scored[idx] = (c, final);
        }

        Array.Sort(scored, (a, b) => b.Final.CompareTo(a.Final));
        var output = new List<PartitionReranker.Reranked>(candidates.Count);
        foreach (var s in scored) output.Add(s.Cand);
        for (int i = N; i < candidates.Count; i++) output.Add(candidates[i]);
        return output;
    }
}
