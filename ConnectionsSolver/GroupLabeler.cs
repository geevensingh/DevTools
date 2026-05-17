namespace ConnectionsSolver;

/// <summary>
/// Pre-normalized vocabulary used to label groups. Building this scans the embeddings
/// once and normalizes each candidate vector, which is much faster than re-normalizing
/// on every Label() call.
/// </summary>
public sealed class LabelingContext
{
    public string[] Words { get; }
    public float[][] UnitVectors { get; }

    public LabelingContext(IWordEmbeddings emb, int vocabSize)
    {
        var pairs = emb.MostFrequent(vocabSize).ToList();
        Words = pairs.Select(p => p.Word).ToArray();
        UnitVectors = pairs.Select(p => Similarity.Normalize(p.Vector)).ToArray();
    }
}

public static class GroupLabeler
{
    /// <summary>
    /// Returns the top <paramref name="topK"/> vocabulary words whose vector is closest
    /// (by cosine similarity) to the centroid of the group's vectors, excluding any input
    /// word or simple morphological variants thereof.
    /// </summary>
    public static List<(string Word, float Score)> Label(
        IReadOnlyList<float[]> vectors,
        IEnumerable<string> excludeWords,
        LabelingContext ctx,
        int topK = 3)
    {
        var centroid = Similarity.Centroid(vectors);
        var centroidUnit = Similarity.Normalize(centroid);
        var excludeSet = BuildExcludeSet(excludeWords);

        var heap = new PriorityQueue<string, float>();
        for (int i = 0; i < ctx.Words.Length; i++)
        {
            var word = ctx.Words[i];
            if (excludeSet.Contains(word)) continue;
            var score = Similarity.Dot(centroidUnit, ctx.UnitVectors[i]);

            if (heap.Count < topK)
            {
                heap.Enqueue(word, score);
            }
            else if (heap.TryPeek(out _, out var minScore) && score > minScore)
            {
                heap.Dequeue();
                heap.Enqueue(word, score);
            }
        }

        var results = new List<(string, float)>(heap.Count);
        while (heap.TryDequeue(out var w, out var s))
            results.Add((w, s));
        results.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return results;
    }

    internal static HashSet<string> BuildExcludeSet(IEnumerable<string> input)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in input)
        {
            var lower = raw.ToLowerInvariant();
            // Multi-word entries: also exclude each constituent so labels don't trivially echo a token.
            foreach (var word in lower.Split(' ', StringSplitOptions.RemoveEmptyEntries).Append(lower))
            {
                if (word.Contains(' ')) continue; // skip the whole multi-word string; vocab is single words
                result.Add(word);

                // Common English morphological variants — keeps labels from being plurals of inputs.
                void Add(string s) { if (!string.IsNullOrEmpty(s)) result.Add(s); }

                if (word.EndsWith("s") && word.Length > 1) Add(word[..^1]);
                Add(word + "s");

                if (word.EndsWith("es") && word.Length > 2) Add(word[..^2]);
                Add(word + "es");

                if (word.EndsWith("ed") && word.Length > 2) Add(word[..^2]);
                Add(word + "ed");

                if (word.EndsWith("ing") && word.Length > 3) Add(word[..^3]);
                Add(word + "ing");

                if (word.EndsWith("y") && word.Length > 1) Add(word[..^1] + "ies");

                if (word.EndsWith("ly") && word.Length > 2) Add(word[..^2]);
            }
        }
        return result;
    }
}
