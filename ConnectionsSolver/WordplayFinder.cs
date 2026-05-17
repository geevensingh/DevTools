namespace ConnectionsSolver;

/// <summary>
/// Finds groups of 4 active input words that share a suffix or prefix wordplay pattern.
/// For each input word, enumerates all (prefix, suffix) splits where one side is a real
/// English word (top-N vocab). Then searches for 4-tuples of input words whose extracted
/// affix words form a tight cluster in GloVe space.
///
/// Example: BASSOON → SOON, BELFAST → FAST, NESQUICK → QUICK, THERMOSTAT → STAT
/// (all suffixes are synonyms for "ASAP"; affix words cluster tightly).
/// </summary>
public static class WordplayFinder
{
    public sealed record Finding(
        string[] InputWords,
        string[] Affixes,
        bool IsSuffix,
        double Tightness);

    public static List<Finding> Find(
        IReadOnlyList<string> activeOriginalEntries,
        LabelingContext vocab,
        int maxResults = 5,
        int minAffixLen = 4,
        double minTightness = 0.20)
    {
        // Vocab dict for affix lookup: lowercase word -> unit vector.
        var vocabDict = new Dictionary<string, float[]>(StringComparer.Ordinal);
        for (int i = 0; i < vocab.Words.Length; i++)
            vocabDict[vocab.Words[i]] = vocab.UnitVectors[i];

        // For each input, find all (affix, vector) candidates on each side.
        int n = activeOriginalEntries.Count;
        var suffixCands = new List<(string Affix, float[] Vec)>[n];
        var prefixCands = new List<(string Affix, float[] Vec)>[n];
        for (int i = 0; i < n; i++)
        {
            suffixCands[i] = new();
            prefixCands[i] = new();
            var raw = activeOriginalEntries[i].ToLowerInvariant();
            if (raw.Contains(' ')) continue; // skip multi-word entries
            // Enumerate suffix splits: word = head + suffix, suffix len >= minAffixLen.
            for (int cut = 1; cut <= raw.Length - minAffixLen; cut++)
            {
                var suffix = raw[cut..];
                if (suffix.Length < minAffixLen) continue;
                if (vocabDict.TryGetValue(suffix, out var v))
                    suffixCands[i].Add((suffix, v));
            }
            // Enumerate prefix splits: word = prefix + tail, prefix len >= minAffixLen.
            for (int cut = minAffixLen; cut < raw.Length; cut++)
            {
                var prefix = raw[..cut];
                if (prefix.Length < minAffixLen) continue;
                if (vocabDict.TryGetValue(prefix, out var v))
                    prefixCands[i].Add((prefix, v));
            }
        }

        var findings = new List<Finding>();
        SearchAffixGroups(n, activeOriginalEntries, suffixCands, isSuffix: true, minTightness, findings);
        SearchAffixGroups(n, activeOriginalEntries, prefixCands, isSuffix: false, minTightness, findings);

        // De-duplicate and pick disjoint top findings. False-positive wordplay groups
        // typically share 3 of 4 words with the true positive; requiring disjoint input
        // sets across emitted findings eliminates near-duplicates while still allowing
        // multiple genuine wordplay groups (which would necessarily be disjoint).
        var deduped = new List<Finding>();
        var usedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in findings.OrderByDescending(f => f.Tightness))
        {
            if (f.InputWords.Any(w => usedWords.Contains(w))) continue;
            deduped.Add(f);
            foreach (var w in f.InputWords) usedWords.Add(w);
            if (deduped.Count >= maxResults) break;
        }
        return deduped;
    }

    private static void SearchAffixGroups(
        int n,
        IReadOnlyList<string> entries,
        List<(string Affix, float[] Vec)>[] cands,
        bool isSuffix,
        double minTightness,
        List<Finding> output)
    {
        var withCands = new List<int>();
        for (int i = 0; i < n; i++) if (cands[i].Count > 0) withCands.Add(i);
        if (withCands.Count < 4) return;

        int m = withCands.Count;
        // Enumerate 4-combinations of words that all have affix candidates.
        for (int a = 0; a < m - 3; a++)
            for (int b = a + 1; b < m - 2; b++)
                for (int c = b + 1; c < m - 1; c++)
                    for (int d = c + 1; d < m; d++)
                    {
                        int wa = withCands[a], wb = withCands[b], wc = withCands[c], wd = withCands[d];
                        // For each affix-per-word product, compute tightness and keep best per combo.
                        double bestTight = double.NegativeInfinity;
                        string[]? bestAffixes = null;
                        foreach (var (sa, va) in cands[wa])
                            foreach (var (sb, vb) in cands[wb])
                                foreach (var (sc, vc) in cands[wc])
                                    foreach (var (sd, vd) in cands[wd])
                                    {
                                        double s = Similarity.Dot(va, vb) + Similarity.Dot(va, vc) + Similarity.Dot(va, vd)
                                                 + Similarity.Dot(vb, vc) + Similarity.Dot(vb, vd) + Similarity.Dot(vc, vd);
                                        double avg = s / 6.0;
                                        // Penalise trivial cases where all 4 affixes are identical word.
                                        if (sa == sb && sb == sc && sc == sd) continue;
                                        if (avg > bestTight)
                                        {
                                            bestTight = avg;
                                            bestAffixes = new[] { sa, sb, sc, sd };
                                        }
                                    }
                        if (bestAffixes != null && bestTight >= minTightness)
                        {
                            output.Add(new Finding(
                                new[] { entries[wa], entries[wb], entries[wc], entries[wd] },
                                bestAffixes, isSuffix, bestTight));
                        }
                    }
    }
}
