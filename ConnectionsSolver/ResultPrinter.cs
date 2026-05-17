namespace ConnectionsSolver;

public static class ResultPrinter
{
    public static void PrintAnalysis(AnalysisResult r)
    {
        if (r.NearMissFollowups.Count > 0)
        {
            Section("NEAR-MISS FOLLOW-UPS (your 'one-away' guesses + best single-word swaps)");
            for (int i = 0; i < r.NearMissFollowups.Count; i++)
                PrintNearMiss(i + 1, r.NearMissFollowups[i]);
        }

        Section("ANCHOR GROUPS OF 4 (disjoint top picks; each anchor shown with single-word-swap variants)");
        if (r.Anchored4.Count == 0)
            Console.WriteLine("  (need at least 4 active words to form a group of 4)");
        for (int i = 0; i < r.Anchored4.Count; i++)
            PrintAnchored("A", i + 1, r.Anchored4[i]);

        Section("ANCHOR GROUPS OF 3 (with best-guess 4th word and single-word-swap variants)");
        for (int i = 0; i < r.Anchored3.Count; i++)
            PrintAnchored("B", i + 1, r.Anchored3[i]);

        if (r.DenseClusters.Count > 0)
        {
            Section("DENSE WORD CLUSTERS (5+ words look related - exactly 4 form a real group; pick carefully)");
            for (int i = 0; i < r.DenseClusters.Count; i++)
                PrintCluster(r.DenseClusters[i]);
        }

        if (r.WordplayGroups.Count > 0)
        {
            Section("WORDPLAY (SUFFIX / PREFIX PATTERNS)");
            for (int i = 0; i < r.WordplayGroups.Count; i++)
                PrintWordplay(r.WordplayGroups[i]);
        }

        if (r.PhrasePatterns.Count > 0)
        {
            Section("PHRASE PATTERNS (SHARED BIGRAM MODIFIER)");
            for (int i = 0; i < r.PhrasePatterns.Count; i++)
                PrintPhrasePattern(r.PhrasePatterns[i]);
        }

        Section("BEST FULL PARTITIONS");
        if (r.Partitions.Count == 0)
            Console.WriteLine("  (not attempted - requires the active-word count to be 8, 12, or 16)");
        for (int i = 0; i < r.Partitions.Count; i++)
            PrintPartition(i + 1, r.Partitions[i]);
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine(title);
        Console.WriteLine(new string('=', 72));
    }

    private static void PrintAnchored(string prefix, int rank, AnchoredGroup ag)
    {
        var label = $"{prefix}{rank}";
        Console.WriteLine();
        Console.Write($"  [{label,-4}]  ");
        PrintCandidateBody(ag.Anchor, anchorIndices: null);
        if (ag.Variants.Count == 0) return;

        var anchorSet = new HashSet<int>(ag.Anchor.WordIndices);
        Console.WriteLine($"           variants (swap one word):");
        for (int v = 0; v < ag.Variants.Count; v++)
        {
            var vlabel = $"{label}v{v + 1}";
            Console.Write($"           [{vlabel,-6}]  ");
            PrintCandidateBody(ag.Variants[v], anchorIndices: anchorSet, indent: "                       ");
        }
    }

    private static void PrintNearMiss(int rank, NearMissFollowup nm)
    {
        Console.WriteLine();
        Console.WriteLine($"  [N{rank}]  one-away set: {string.Join(", ", nm.OriginalWords)}");
        if (nm.Swaps.Count == 0)
        {
            Console.WriteLine($"        (fewer than 3 of these 4 words remain active; no swaps possible)");
            return;
        }
        Console.WriteLine($"        4 single-word swaps (ranked by score):");
        for (int s = 0; s < nm.Swaps.Count; s++)
        {
            var label = $"N{rank}s{s + 1}";
            var swap = nm.Swaps[s];
            Console.Write($"          [{label,-5}]  swap out '{swap.RemovedWord}'  ");
            PrintCandidateBody(swap.Replacement, anchorIndices: null, indent: "                       ");
        }
    }

    /// <summary>
    /// Prints a single group's score + labels + words. When <paramref name="anchorIndices"/>
    /// is provided, words not in the anchor set are bracketed to highlight the swap.
    /// </summary>
    private static void PrintCandidateBody(CandidateGroup c, HashSet<int>? anchorIndices, string indent = "           ")
    {
        var labels = c.Labels.Length == 0 ? "(no labels)" : string.Join(", ", c.Labels);
        var leftoverNote = c.LeftoverPartitionScore.HasValue
            ? $"  leftover: {c.LeftoverPartitionScore.Value:F3}"
            : "";
        Console.WriteLine($"{Bar(c.AverageSimilarity),-12}  {FormatScore(c.AverageSimilarity),-16}  [{labels}]{leftoverNote}");

        string FormatWord(int idx, string w) =>
            anchorIndices != null && !anchorIndices.Contains(idx) ? $"<{w}>" : w;

        var displayWords = c.WordIndices.Zip(c.Words, FormatWord);
        Console.WriteLine($"{indent}{string.Join(", ", displayWords)}");

        if (c.CandidateFourth is { Count: > 0 })
        {
            Console.WriteLine($"{indent}possible 4th word:");
            foreach (var (w, s) in c.CandidateFourth)
                Console.WriteLine($"{indent}  - {w,-14} {FormatScore(s)}");
        }
    }

    private static void PrintCluster(DenseCluster c)
    {
        Console.WriteLine();
        Console.WriteLine($"  [X{c.Id}]  cluster of {c.Words.Length} words:");
        Console.WriteLine($"           {string.Join(", ", c.Words)}");
        if (c.Subsets.Count == 0)
        {
            Console.WriteLine($"           (no 4-subsets to suggest)");
            return;
        }
        Console.WriteLine($"           suggested 4-subsets (ranked by combined score):");
        for (int s = 0; s < c.Subsets.Count; s++)
        {
            var slabel = $"X{c.Id}s{s + 1}";
            Console.Write($"           [{slabel,-6}]  ");
            PrintCandidateBody(c.Subsets[s], anchorIndices: null,
                               indent: "                       ");
        }
    }

    private static void PrintWordplay(WordplayGroup w)
    {
        var kind = w.IsSuffix ? "suffix" : "prefix";
        Console.WriteLine();
        Console.WriteLine($"  [W{w.Id}]  shared {kind} theme  {Bar(w.Tightness),-12}  {FormatScore(w.Tightness)}");
        for (int i = 0; i < w.InputWords.Length; i++)
        {
            var word = w.InputWords[i];
            var affix = w.Affixes[i];
            if (w.IsSuffix)
            {
                int cut = word.ToLowerInvariant().LastIndexOf(affix.ToLowerInvariant());
                var head = cut > 0 ? word.Substring(0, cut) : "";
                Console.WriteLine($"           {word,-20} = {head} + [{affix}]");
            }
            else
            {
                int cut = affix.Length;
                var tail = cut < word.Length ? word.Substring(cut) : "";
                Console.WriteLine($"           {word,-20} = [{affix}] + {tail}");
            }
        }
    }

    private static void PrintPhrasePattern(PhrasePatternGroup p)
    {
        Console.WriteLine();
        var template = p.IsRight
            ? $"\"___ {p.Modifier.ToUpperInvariant()}\""
            : $"\"{p.Modifier.ToUpperInvariant()} ___\"";
        Console.WriteLine($"  [P{p.Id}]  shared phrase pattern: {template}    (geomean count {p.Score:N0})");
        int maxW = 0;
        foreach (var w in p.InputWords) if (w.Length > maxW) maxW = w.Length;
        for (int i = 0; i < p.InputWords.Length; i++)
        {
            var word = p.InputWords[i].ToLowerInvariant();
            var count = p.BigramCounts[i];
            var phrase = p.IsRight
                ? $"\"{word} {p.Modifier}\""
                : $"\"{p.Modifier} {word}\"";
            Console.WriteLine($"           {p.InputWords[i].PadRight(maxW)}    {phrase,-32}  ({count:N0})");
        }
    }

    private static void PrintPartition(int rank, LabeledPartition p)
    {
        Console.WriteLine();
        Console.WriteLine($"  Partition #{rank}    total score: {p.TotalScore:F3}");
        for (int i = 0; i < p.Groups.Length; i++)
        {
            var g = p.Groups[i];
            var labels = g.Labels.Length == 0 ? "(no labels)" : string.Join(", ", g.Labels);
            Console.WriteLine($"    Group {i + 1}  {Bar(g.AverageSimilarity),-12}  {FormatScore(g.AverageSimilarity),-16}  [{labels}]");
            Console.WriteLine($"             {string.Join(", ", g.Words)}");
        }
    }

    private static string FormatScore(double avgSim)
    {
        var pct = (int)System.Math.Round(System.Math.Clamp(avgSim, 0.0, 1.0) * 100);
        return $"{avgSim:F3} ({pct}%)";
    }

    private static string Bar(double avgSim)
    {
        int stars = (int)System.Math.Round(System.Math.Clamp(avgSim, 0.0, 1.0) * 5);
        return new string('*', stars) + new string('-', 5 - stars);
    }
}

