namespace ConnectionsSolver;

public enum FeedbackVerdict
{
    Correct,
    NotAGroup,
    OneAway,
}

public sealed record FeedbackResult(
    int[] KnownIndices,
    string[] MissingWords,
    FeedbackVerdict Verdict);

public static class Feedback
{
    /// <summary>
    /// Parses one feedback line. Returns null when the line could not be interpreted.
    /// Accepted forms (case-insensitive; tokens separated by whitespace or commas):
    ///   &lt;label&gt; &lt;verdict&gt;
    ///   &lt;word1&gt; &lt;word2&gt; &lt;word3&gt; &lt;word4&gt; &lt;verdict&gt;
    /// Words may be in-vocabulary (active known words) or out-of-vocabulary input
    /// words (e.g. brand names or typos that weren't in GloVe). For label-based
    /// input, the matched words are always in-vocabulary.
    /// Verdict tokens at the end of the line:
    ///   yes | y | correct | right          → Correct
    ///   no  | n | wrong  | incorrect       → NotAGroup
    ///   off | close | near | 1away | oneaway | off-by-one → OneAway
    ///   off by one                          → OneAway (three-token form)
    /// Multi-word entries: use commas to separate entries, e.g.
    ///   free love, hippie, acid, commune yes
    /// </summary>
    public static FeedbackResult? Parse(
        string input,
        IReadOnlyDictionary<string, int[]> labelMap,
        IReadOnlyDictionary<string, string[]> labelOriginalMap,
        IReadOnlyDictionary<string, int> activeWordToKnownIndex,
        IReadOnlySet<string> activeMissingWords,
        out string? error)
    {
        error = null;

        // If the user used commas, treat as comma-separated entries (supports multi-word entries).
        if (input.Contains(','))
            return ParseCommaSeparated(input, activeWordToKnownIndex, activeMissingWords, out error);

        var tokens = input.ToLowerInvariant()
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            error = "Need at least an identifier and a verdict (yes/no/off-by-one).";
            return null;
        }

        // Strip trailing verdict token(s).
        if (!TryStripVerdict(tokens, out var verdict, out int idTokenCount, out error))
            return null;

        if (idTokenCount <= 0)
        {
            error = "Missing identifier (label or 4 words).";
            return null;
        }

        int[]? knownIndices = null;
        string[]? missingWords = null;
        if (idTokenCount == 1 && labelMap.TryGetValue(tokens[0], out var labelIndices))
        {
            knownIndices = labelIndices;
            missingWords = Array.Empty<string>();
        }
        else if (idTokenCount == 1 && labelOriginalMap.TryGetValue(tokens[0], out var labelOriginals))
        {
            // Label resolves to a mix of vocab and out-of-vocab entries (e.g. wordplay
            // groups that include a brand name not in GloVe). Resolve each entry.
            var resolvedKnown = new List<int>(4);
            var resolvedMissing = new List<string>(4);
            foreach (var raw in labelOriginals)
            {
                var t = raw.ToLowerInvariant();
                if (activeWordToKnownIndex.TryGetValue(t, out var ki)) resolvedKnown.Add(ki);
                else if (activeMissingWords.Contains(t)) resolvedMissing.Add(t);
                else
                {
                    error = $"Label '{tokens[0]}' references inactive or unknown word '{t}'.";
                    return null;
                }
            }
            knownIndices = resolvedKnown.ToArray();
            missingWords = resolvedMissing.ToArray();
        }
        else if (idTokenCount == 4)
        {
            var resolvedKnown = new List<int>(4);
            var resolvedMissing = new List<string>(4);
            var seenKnown = new HashSet<int>();
            var seenMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 4; i++)
            {
                var t = tokens[i];
                if (activeWordToKnownIndex.TryGetValue(t, out var ki))
                {
                    if (!seenKnown.Add(ki))
                    {
                        error = "Duplicate words in guess.";
                        return null;
                    }
                    resolvedKnown.Add(ki);
                }
                else if (activeMissingWords.Contains(t))
                {
                    if (!seenMissing.Add(t))
                    {
                        error = "Duplicate words in guess.";
                        return null;
                    }
                    resolvedMissing.Add(t);
                }
                else
                {
                    error = $"Unknown or inactive word: '{t}'.";
                    return null;
                }
            }
            knownIndices = resolvedKnown.ToArray();
            missingWords = resolvedMissing.ToArray();
        }
        else if (idTokenCount == 1)
        {
            error = $"Unknown label: '{tokens[0]}'.";
            return null;
        }
        else
        {
            error = $"Expected a single label or exactly 4 words; got {idTokenCount} identifier token(s).";
            return null;
        }

        int total = knownIndices.Length + missingWords.Length;
        if (total != 4)
        {
            error = $"Guess must be exactly 4 words; got {total}.";
            return null;
        }

        return new FeedbackResult(knownIndices, missingWords, verdict);
    }

    private static bool TryStripVerdict(
        string[] tokens,
        out FeedbackVerdict verdict,
        out int idTokenCount,
        out string? error)
    {
        verdict = default;
        idTokenCount = 0;
        error = null;
        if (tokens.Length >= 4 && tokens[^3] == "off" && tokens[^2] == "by" && tokens[^1] == "one")
        {
            verdict = FeedbackVerdict.OneAway;
            idTokenCount = tokens.Length - 3;
            return true;
        }
        switch (tokens[^1])
        {
            case "yes": case "y": case "correct": case "right":
                verdict = FeedbackVerdict.Correct; break;
            case "no": case "n": case "wrong": case "incorrect":
                verdict = FeedbackVerdict.NotAGroup; break;
            case "off": case "close": case "near": case "1away": case "oneaway": case "off-by-one":
                verdict = FeedbackVerdict.OneAway; break;
            default:
                error = $"Last token must be a verdict (yes/no/off-by-one); got '{tokens[^1]}'.";
                return false;
        }
        idTokenCount = tokens.Length - 1;
        return true;
    }

    private static FeedbackResult? ParseCommaSeparated(
        string input,
        IReadOnlyDictionary<string, int> activeWordToKnownIndex,
        IReadOnlySet<string> activeMissingWords,
        out string? error)
    {
        error = null;
        var commaParts = input.ToLowerInvariant()
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => System.Text.RegularExpressions.Regex.Replace(p.Trim(), @"\s+", " "))
            .Where(p => p.Length > 0)
            .ToArray();
        if (commaParts.Length == 0)
        {
            error = "Empty input.";
            return null;
        }

        // Verdict lives at the end of the last comma-segment.
        var lastTokens = commaParts[^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (lastTokens.Length < 2)
        {
            error = "Last comma-segment must contain the final entry followed by a verdict (yes/no/off-by-one).";
            return null;
        }
        if (!TryStripVerdict(lastTokens, out var verdict, out int lastIdTokens, out error))
            return null;
        if (lastIdTokens < 1)
        {
            error = "Missing final entry before the verdict.";
            return null;
        }

        var entries = new List<string>(commaParts.Length);
        for (int i = 0; i < commaParts.Length - 1; i++)
            entries.Add(commaParts[i]);
        entries.Add(string.Join(' ', lastTokens.Take(lastIdTokens)));

        if (entries.Count != 4)
        {
            error = $"Expected exactly 4 comma-separated entries before the verdict; got {entries.Count}.";
            return null;
        }

        var resolvedKnown = new List<int>(4);
        var resolvedMissing = new List<string>(4);
        var seenKnown = new HashSet<int>();
        var seenMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (activeWordToKnownIndex.TryGetValue(e, out var ki))
            {
                if (!seenKnown.Add(ki)) { error = "Duplicate entries in guess."; return null; }
                resolvedKnown.Add(ki);
            }
            else if (activeMissingWords.Contains(e))
            {
                if (!seenMissing.Add(e)) { error = "Duplicate entries in guess."; return null; }
                resolvedMissing.Add(e);
            }
            else
            {
                error = $"Unknown or inactive entry: '{e}'.";
                return null;
            }
        }

        return new FeedbackResult(resolvedKnown.ToArray(), resolvedMissing.ToArray(), verdict);
    }
}

