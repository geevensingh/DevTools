namespace ConnectionsSolver;

public static class InteractiveSession
{
    public sealed record Options(
        int AnchorCount,
        int VariantsPerAnchor,
        int TopPartitions,
        int LabelCount,
        int FourthCandidates,
        double LeftoverAlpha,
        int RerankTopN);

    public static void Run(
        string[] originalWords,
        string[] knownWords,
        int[] knownToOriginalIndex,
        IReadOnlyList<string> missingWords,
        float[][] unitVectors,
        LabelingContext labelCtx,
        BigramData? bigrams,
        Options opt)
    {
        var solved = new HashSet<int>();                                              // known-words indices marked correct
        var solvedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);    // out-of-vocab words marked correct
        var solvedGroups = new List<int[]>();                                          // history (known indices only; display)
        var forbidden = new HashSet<string>();                                         // canonical keys (Solver.CanonicalKey)
        var nearMissSets = new List<int[]>();                                          // sorted known-indices arrays
        int roundNumber = 0;
        var allMissingSet = new HashSet<string>(missingWords, StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            roundNumber++;
            var active = Enumerable.Range(0, knownWords.Length)
                .Where(i => !solved.Contains(i))
                .ToArray();
            int activeN = active.Length;

            Console.WriteLine();
            Console.WriteLine(new string('#', 72));
            Console.WriteLine($"# Round {roundNumber}: {activeN} of {knownWords.Length} known word(s) remaining"
                              + (solvedGroups.Count > 0 ? $", {solvedGroups.Count} group(s) solved" : ""));
            Console.WriteLine(new string('#', 72));

            PrintBoard(originalWords, knownWords, knownToOriginalIndex, missingWords, solved, solvedMissing);

            if (activeN == 0)
            {
                Console.WriteLine();
                Console.WriteLine("All known words placed. Done.");
                return;
            }
            if (activeN < 3)
            {
                Console.WriteLine();
                Console.WriteLine($"Only {activeN} active word(s) left; not enough to form a group of 3 or 4.");
                return;
            }
            if (activeN == 4)
            {
                Console.WriteLine();
                Console.WriteLine("Only 4 active words remain - this must be the last group:");
                Console.WriteLine("    " + string.Join(", ", active.Select(i => knownWords[i])));
            }

            // Active original entries (including missing/OOV words): used by wordplay detection.
            var solvedOriginalIdx = new HashSet<int>();
            foreach (var ki in solved) solvedOriginalIdx.Add(knownToOriginalIndex[ki]);
            var activeOriginalEntries = new List<string>();
            for (int oi = 0; oi < originalWords.Length; oi++)
            {
                if (solvedOriginalIdx.Contains(oi)) continue;
                if (solvedMissing.Contains(originalWords[oi])) continue;
                activeOriginalEntries.Add(originalWords[oi]);
            }

            var result = Solver.Analyze(
                active, knownWords, unitVectors, originalWords, activeOriginalEntries, labelCtx, bigrams,
                forbidden, nearMissSets,
                opt.AnchorCount, opt.VariantsPerAnchor, opt.TopPartitions,
                opt.LabelCount, opt.FourthCandidates,
                opt.LeftoverAlpha, opt.RerankTopN);

            ResultPrinter.PrintAnalysis(result);

            var wordToKnown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var ki in active) wordToKnown[knownWords[ki]] = ki;
            var activeMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in allMissingSet)
                if (!solvedMissing.Contains(w)) activeMissing.Add(w);

            if (!Prompt(result, wordToKnown, activeMissing, knownWords, solved, solvedMissing, solvedGroups, forbidden, nearMissSets))
                return;
        }
    }

    private static bool Prompt(
        AnalysisResult result,
        Dictionary<string, int> wordToKnown,
        IReadOnlySet<string> activeMissing,
        string[] knownWords,
        HashSet<int> solved,
        HashSet<string> solvedMissing,
        List<int[]> solvedGroups,
        HashSet<string> forbidden,
        List<int[]> nearMissSets)
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("What did you try?  <label or 4 words> <yes | no | off-by-one>");
            Console.WriteLine("  e.g.  A1 yes   |   A2v1 no   |   N1s3 yes   |   X1s2 off   |   W1 yes   |   P1 yes   |   piano violin guitar drums off");
            Console.WriteLine("  ('skip' to recompute without feedback, 'quit' to exit)");
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line == null) return false;
            line = line.Trim();
            if (line.Length == 0) continue;

            if (line.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("exit", StringComparison.OrdinalIgnoreCase))
                return false;
            if (line.Equals("skip", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Equals("help", StringComparison.OrdinalIgnoreCase) ||
                line == "?")
            {
                Console.WriteLine("Verdict tokens:");
                Console.WriteLine("  Correct:    yes | y | correct | right");
                Console.WriteLine("  Not group:  no  | n | wrong | incorrect");
                Console.WriteLine("  One-away:   off | close | near | 1away | oneaway | off-by-one | off by one");
                continue;
            }

            var fb = Feedback.Parse(line, result.LabelToKnownIndices, result.LabelToOriginalEntries, wordToKnown, activeMissing, out var err);
            if (fb == null)
            {
                Console.WriteLine($"  {err}  (type 'help' for grammar)");
                continue;
            }

            var sorted = (int[])fb.KnownIndices.Clone();
            Array.Sort(sorted);
            var key = Solver.CanonicalKey(sorted);
            var knownDisplay = fb.KnownIndices.Select(i => knownWords[i]);
            var displayWords = knownDisplay.Concat(fb.MissingWords).ToArray();
            bool hasMissing = fb.MissingWords.Length > 0;

            switch (fb.Verdict)
            {
                case FeedbackVerdict.Correct:
                    foreach (var i in fb.KnownIndices) solved.Add(i);
                    foreach (var w in fb.MissingWords) solvedMissing.Add(w);
                    solvedGroups.Add(fb.KnownIndices);
                    Console.WriteLine($"  Recorded group #{solvedGroups.Count}: {string.Join(", ", displayWords)}");
                    return true;

                case FeedbackVerdict.NotAGroup:
                    if (hasMissing)
                    {
                        Console.WriteLine($"  Noted not-a-group: {string.Join(", ", displayWords)}");
                        Console.WriteLine($"  (cannot refine future suggestions: out-of-vocab word(s) [{string.Join(", ", fb.MissingWords)}] aren't in any candidate I generate)");
                    }
                    else
                    {
                        forbidden.Add(key);
                        Console.WriteLine($"  Recorded as not-a-group: {string.Join(", ", displayWords)}");
                    }
                    return true;

                case FeedbackVerdict.OneAway:
                    if (hasMissing)
                    {
                        Console.WriteLine($"  Noted one-away: {string.Join(", ", displayWords)}");
                        Console.WriteLine($"  (cannot generate swap suggestions: out-of-vocab word(s) [{string.Join(", ", fb.MissingWords)}] have no embedding)");
                    }
                    else
                    {
                        forbidden.Add(key);
                        if (!nearMissSets.Any(s => s.SequenceEqual(sorted)))
                            nearMissSets.Add(sorted);
                        Console.WriteLine($"  Recorded as one-away (near-miss): {string.Join(", ", displayWords)}");
                    }
                    return true;
            }
            return true;
        }
    }

    private static void PrintBoard(
        string[] originalWords,
        string[] knownWords,
        int[] knownToOriginalIndex,
        IReadOnlyList<string> missingWords,
        HashSet<int> solved,
        HashSet<string> solvedMissing)
    {
        // Reverse lookup: originalIndex → status
        var status = new char[originalWords.Length]; // 'A'=active, 'S'=solved-known, 'M'=solved-missing, 'X'=unsolved-missing
        for (int oi = 0; oi < originalWords.Length; oi++) status[oi] = 'A';
        var missingSet = new HashSet<string>(missingWords, StringComparer.OrdinalIgnoreCase);
        for (int oi = 0; oi < originalWords.Length; oi++)
            if (missingSet.Contains(originalWords[oi]))
                status[oi] = solvedMissing.Contains(originalWords[oi]) ? 'M' : 'X';
        for (int ki = 0; ki < knownWords.Length; ki++)
            if (solved.Contains(ki)) status[knownToOriginalIndex[ki]] = 'S';

        int maxW = 0;
        foreach (var w in originalWords)
            if (w.Length > maxW) maxW = w.Length;
        int col = Math.Max(14, maxW + 1);
        bool hasMultiWord = originalWords.Any(w => w.Contains(' '));
        int wrapAt = hasMultiWord && col > 18 ? 2 : 4;

        Console.WriteLine();
        Console.WriteLine("  Legend: [ ] active, [x] solved, [-] not in vocabulary");
        for (int i = 0; i < originalWords.Length; i++)
        {
            var marker = status[i] switch
            {
                'S' => "[x]",
                'M' => "[x]",
                'X' => "[-]",
                _ => "[ ]",
            };
            Console.Write($"  {marker} {originalWords[i].PadRight(col)}");
            if ((i + 1) % wrapAt == 0) Console.WriteLine();
        }
    }
}
