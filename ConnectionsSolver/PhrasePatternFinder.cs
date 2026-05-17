namespace ConnectionsSolver;

/// <summary>
/// Phase 5: finds groups of 4 active entries that share a single bigram modifier on
/// the same side. E.g., {FRENCH, GREEN, INDUSTRIAL, SEXUAL} all commonly appear as
/// "X revolution"; {ARTIST, GAME, STICKS, TRUCK} all commonly appear as "pick-up X"
/// (subject to whether the bigram corpus tokenizes "pick-up" as a single word).
/// </summary>
public static class PhrasePatternFinder
{
    // Closed-class stopwords that should never be the "shared modifier". These would
    // otherwise produce false positives (e.g. "the X" matches half the active words).
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","of","to","and","in","for","on","at","by","with","from",
        "this","that","these","those",
        "is","are","was","were","be","been","being","am",
        "has","have","had","having",
        "will","would","could","can","may","might","must","should","shall",
        "do","does","did","doing","done",
        "not","but","or","if","so","as","it","its","their","they","them","we","us","you","i","he","she","my","our","your","his","her",
        "who","what","when","where","why","how","which",
        "all","any","both","each","few","more","most","other","some","such","no","nor","only","own","same","than","too","very",
        "just","now","also","then","here","there",
        "up","down","out","off","over","under","again","further","once",
        "into","through","during","before","after","above","below","between","about","against","across","along","around","behind","beyond","beside",
        "s","t","d","ll","m","re","ve","o",
    };

    public sealed record Finding(
        string Modifier,
        bool IsRight,
        string[] Entries,
        long[] BigramCounts,
        double Score);

    /// <summary>
    /// For each active entry, retrieves the top-K left and right bigram modifiers
    /// (filtered by stopwords), pivots into modifier→entries, and emits findings
    /// where 4+ entries share the same modifier on the same side. Findings are
    /// scored by geometric mean of the bigram counts (penalizes uneven matches).
    /// Multi-word entries: left mods come from the first token, right mods from the last.
    /// </summary>
    public static IReadOnlyList<Finding> Find(
        IReadOnlyList<string> activeOriginalEntries,
        BigramData bigrams,
        int topKPerWord = 50,
        long minBigramCount = 100,
        int maxResults = 5)
    {
        if (activeOriginalEntries.Count < 4) return Array.Empty<Finding>();

        // candidates[(modifier, isRight)] = list of (entry, bigramCount).
        // isRight=true means the modifier comes AFTER the entry (entry + " " + modifier).
        var candidates = new Dictionary<(string Mod, bool IsRight), List<(string Entry, long Count)>>();

        foreach (var entry in activeOriginalEntries)
        {
            var tokens = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            var firstTok = tokens[0].ToLowerInvariant();
            var lastTok = tokens[^1].ToLowerInvariant();

            foreach (var (m, c) in bigrams.GetNextWords(lastTok, topKPerWord))
            {
                if (c < minBigramCount) break;
                if (!IsAcceptableModifier(m)) continue;
                var key = (m, true);
                if (!candidates.TryGetValue(key, out var list)) { list = new(); candidates[key] = list; }
                list.Add((entry, c));
            }
            foreach (var (m, c) in bigrams.GetPrevWords(firstTok, topKPerWord))
            {
                if (c < minBigramCount) break;
                if (!IsAcceptableModifier(m)) continue;
                var key = (m, false);
                if (!candidates.TryGetValue(key, out var list)) { list = new(); candidates[key] = list; }
                list.Add((entry, c));
            }
        }

        var emitted = new List<Finding>();
        foreach (var kv in candidates)
        {
            if (kv.Value.Count < 4) continue;
            // Multiple entries may share the same first/last token (rare); dedupe by entry.
            var byEntry = kv.Value
                .GroupBy(t => t.Entry, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Entry: g.Key, Count: g.Max(x => x.Count)))
                .OrderByDescending(t => t.Count)
                .Take(4)
                .ToList();
            if (byEntry.Count < 4) continue;

            double score = GeometricMean(byEntry.Select(t => (double)t.Count));
            emitted.Add(new Finding(
                Modifier: kv.Key.Mod,
                IsRight: kv.Key.IsRight,
                Entries: byEntry.Select(t => t.Entry).ToArray(),
                BigramCounts: byEntry.Select(t => t.Count).ToArray(),
                Score: score));
        }

        emitted.Sort((a, b) => b.Score.CompareTo(a.Score));

        // Disjoint filtering: false-positive patterns often share 3 of 4 entries with the
        // true positive. Same approach as WordplayFinder.
        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filtered = new List<Finding>();
        foreach (var f in emitted)
        {
            bool overlap = false;
            foreach (var e in f.Entries) if (seenEntries.Contains(e)) { overlap = true; break; }
            if (overlap) continue;
            filtered.Add(f);
            foreach (var e in f.Entries) seenEntries.Add(e);
            if (filtered.Count >= maxResults) break;
        }
        return filtered;
    }

    private static double GeometricMean(IEnumerable<double> xs)
    {
        double logsum = 0;
        int n = 0;
        foreach (var x in xs)
        {
            if (x <= 0) return 0;
            logsum += Math.Log(x);
            n++;
        }
        if (n == 0) return 0;
        return Math.Exp(logsum / n);
    }

    private static bool IsAcceptableModifier(string m)
    {
        if (m.Length < 2) return false;
        if (StopWords.Contains(m)) return false;
        // Reject markup / non-word tokens like "<S>", "<P>", "&amp;", numeric tokens.
        bool hasLetter = false;
        foreach (var c in m)
        {
            if (c == '<' || c == '>' || c == '&' || c == '#' || c == '@' || c == '$') return false;
            if (char.IsLetter(c)) hasLetter = true;
        }
        return hasLetter;
    }
}
