namespace ConnectionsSolver;

/// <summary>
/// Result of running one round of analysis against a (possibly shrunken) active word set.
/// Indices in CandidateGroup.WordIndices are in "known-words" coordinates (stable across rounds);
/// indices in <see cref="LabelToKnownIndices"/> match that same coordinate system.
/// </summary>
public sealed record AnalysisResult(
    IReadOnlyList<AnchoredGroup> Anchored4,
    IReadOnlyList<AnchoredGroup> Anchored3,
    IReadOnlyList<LabeledPartition> Partitions,
    IReadOnlyList<NearMissFollowup> NearMissFollowups,
    IReadOnlyList<DenseCluster> DenseClusters,
    IReadOnlyList<WordplayGroup> WordplayGroups,
    IReadOnlyList<PhrasePatternGroup> PhrasePatterns,
    IReadOnlyDictionary<string, int[]> LabelToKnownIndices,
    IReadOnlyDictionary<string, string[]> LabelToOriginalEntries);

/// <summary>One "off by one" set with its four single-word-swap follow-ups.</summary>
public sealed record NearMissFollowup(string[] OriginalWords, IReadOnlyList<NearMissSwap> Swaps);

public sealed record NearMissSwap(
    string RemovedWord,
    CandidateGroup Replacement);

/// <summary>
/// 5+ active words that all sit in each other's top-K neighborhood; flagged as a
/// "pick carefully" warning. Subsets are the best 4-word picks WITHIN the cluster
/// (already reranked by partition score).
/// </summary>
public sealed record DenseCluster(
    int Id,
    int[] KnownIndices,
    string[] Words,
    IReadOnlyList<CandidateGroup> Subsets);

/// <summary>
/// 4 input words whose suffix or prefix substrings form a tight cluster (e.g. words
/// ending in synonyms of ASAP). May include out-of-vocab input words.
/// </summary>
public sealed record WordplayGroup(
    int Id,
    string[] InputWords,
    string[] Affixes,
    bool IsSuffix,
    double Tightness);

/// <summary>
/// 4 input entries that share a single bigram modifier on the same side (e.g.
/// {FRENCH, GREEN, INDUSTRIAL, SEXUAL} all pair with "revolution"). May include
/// out-of-vocab input entries since detection is based on bigram counts, not GloVe.
/// </summary>
public sealed record PhrasePatternGroup(
    int Id,
    string Modifier,
    bool IsRight,
    string[] InputWords,
    long[] BigramCounts,
    double Score);

public static class Solver
{
    public static AnalysisResult Analyze(
        int[] activeKnownIndices,
        string[] knownWords,
        float[][] unitVectors,
        string[] originalWords,
        IReadOnlyList<string> activeOriginalEntries,
        LabelingContext labelCtx,
        BigramData? bigrams,
        HashSet<string> forbiddenSetKeys,
        IReadOnlyList<int[]> nearMissSets,
        int anchorCount,
        int variantsPerAnchor,
        int topPartitions,
        int labelCount,
        int fourthCandidates,
        double leftoverAlpha,
        int rerankTopN,
        double labelRerankBeta,
        int labelRerankTopN)
    {
        int activeN = activeKnownIndices.Length;
        var activeWords = activeKnownIndices.Select(i => knownWords[i]).ToArray();
        var activeUnits = activeKnownIndices.Select(i => unitVectors[i]).ToArray();
        var activeSim = Similarity.BuildPairwiseMatrix(activeUnits);

        // active-index → position in activeKnownIndices, for translating known-indices back.
        var knownToActive = new Dictionary<int, int>(activeN);
        for (int i = 0; i < activeN; i++) knownToActive[activeKnownIndices[i]] = i;

        bool IsForbidden(int[] activeIdxs)
        {
            var knownIdxs = new int[activeIdxs.Length];
            for (int i = 0; i < activeIdxs.Length; i++) knownIdxs[i] = activeKnownIndices[activeIdxs[i]];
            Array.Sort(knownIdxs);
            return forbiddenSetKeys.Contains(CanonicalKey(knownIdxs));
        }

        var subsets3 = activeN >= 3
            ? SubsetSearch.EnumerateScored(activeN, 3, activeSim)
            : new List<(int[] Indices, double AvgSim)>();
        var subsets4 = activeN >= 4
            ? SubsetSearch.EnumerateScored(activeN, 4, activeSim)
            : new List<(int[] Indices, double AvgSim)>();

        var allScored4 = subsets4.Where(s => !IsForbidden(s.Indices))
            .OrderByDescending(s => s.AvgSim).ToList();
        var allScored3 = subsets3.OrderByDescending(s => s.AvgSim).ToList();

        // Phase 2: partition-score reranking. Reorders allScored4 so that candidates whose
        // removal leaves a good leftover partition are promoted over candidates that "steal"
        // words from other plausible groups.
        var reranked4 = PartitionReranker.Rerank(
            allScored4, activeN, activeSim, activeKnownIndices, forbiddenSetKeys,
            leftoverAlpha, rerankTopN);

        // Phase 6: label-overlap reranking. Adds a centroid-to-label-vocab signal on top
        // of the Phase 2 combined score. Promotes groups with a single dominant theme word
        // (e.g. {grinder, hero, hoagie, sub} → "sandwich") over groups that merely have
        // tight pairwise cosine but no clear unifying label.
        if (labelRerankBeta > 0 && labelRerankTopN > 0)
        {
            reranked4 = LabelOverlapReranker.Rerank(
                reranked4, activeUnits, labelCtx, originalWords,
                labelRerankBeta, labelRerankTopN);
        }

        var ranked4Tuples = reranked4.Select(r => (r.Indices, r.AvgSim)).ToList();
        var leftoverByKey = new Dictionary<string, double>();
        foreach (var r in reranked4)
            if (r.LeftoverPartitionScore.HasValue)
                leftoverByKey[ActiveKey(r.Indices)] = r.LeftoverPartitionScore.Value;

        var anchors4 = PickAnchors(ranked4Tuples, anchorCount);
        var anchors3 = PickAnchors(allScored3, anchorCount);

        var variants4 = anchors4
            .Select(a => PickVariants(a.Indices, ranked4Tuples, variantsPerAnchor))
            .ToList();
        var variants3 = anchors3
            .Select(a => PickVariants(a.Indices, allScored3, variantsPerAnchor))
            .ToList();

        var labelMap = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        var labelOriginalMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        CandidateGroup MakeCand(int[] activeIdxs, double avgSim, IReadOnlyList<(string, double)>? fourth, double? leftoverScore = null)
        {
            var groupWords = activeIdxs.Select(i => activeWords[i]).ToArray();
            var vectors = activeIdxs.Select(i => activeUnits[i]).ToList();
            var labels = GroupLabeler.Label(vectors, originalWords, labelCtx, labelCount)
                .Select(l => l.Word).ToArray();
            var knownIdxs = activeIdxs.Select(i => activeKnownIndices[i]).ToArray();
            return new CandidateGroup(knownIdxs, groupWords, avgSim, labels)
            {
                CandidateFourth = fourth,
                LeftoverPartitionScore = leftoverScore,
            };
        }

        double? LookupLeftover(int[] activeIdxs)
            => leftoverByKey.TryGetValue(ActiveKey(activeIdxs), out var v) ? v : null;

        IReadOnlyList<(string, double)> BestFourth(int[] triplet)
        {
            var inSet = new HashSet<int>(triplet);
            double existing = 0;
            for (int a = 0; a < 3; a++)
                for (int b = a + 1; b < 3; b++)
                    existing += activeSim[triplet[a], triplet[b]];
            var candidates = new List<(string, double)>(activeN);
            for (int i = 0; i < activeN; i++)
            {
                if (inSet.Contains(i)) continue;
                double added = 0;
                for (int j = 0; j < 3; j++) added += activeSim[i, triplet[j]];
                candidates.Add((activeWords[i], (existing + added) / 6.0));
            }
            return candidates.OrderByDescending(c => c.Item2).Take(fourthCandidates).ToList();
        }

        var anchored4 = new List<AnchoredGroup>();
        for (int i = 0; i < anchors4.Count; i++)
        {
            var anchor = MakeCand(anchors4[i].Indices, anchors4[i].AvgSim, fourth: null,
                                   leftoverScore: LookupLeftover(anchors4[i].Indices));
            var label = $"A{i + 1}";
            labelMap[label] = anchor.WordIndices;
            var vars = new List<CandidateGroup>();
            for (int v = 0; v < variants4[i].Count; v++)
            {
                var variant = MakeCand(variants4[i][v].Indices, variants4[i][v].AvgSim, fourth: null,
                                        leftoverScore: LookupLeftover(variants4[i][v].Indices));
                vars.Add(variant);
                labelMap[$"{label}v{v + 1}"] = variant.WordIndices;
            }
            anchored4.Add(new AnchoredGroup(anchor, vars));
        }

        var anchored3 = new List<AnchoredGroup>();
        for (int i = 0; i < anchors3.Count; i++)
        {
            var anchor = MakeCand(anchors3[i].Indices, anchors3[i].AvgSim, fourth: BestFourth(anchors3[i].Indices));
            var label = $"B{i + 1}";
            labelMap[label] = anchor.WordIndices;
            var vars = new List<CandidateGroup>();
            for (int v = 0; v < variants3[i].Count; v++)
            {
                var variant = MakeCand(variants3[i][v].Indices, variants3[i][v].AvgSim, fourth: BestFourth(variants3[i][v].Indices));
                vars.Add(variant);
                labelMap[$"{label}v{v + 1}"] = variant.WordIndices;
            }
            anchored3.Add(new AnchoredGroup(anchor, vars));
        }

        // Near-miss followups: for each "off by one" 4-set still relevant (>=3 of its 4 words active),
        // produce up to 4 single-swap replacements, ranked by score.
        var followups = new List<NearMissFollowup>();
        int nearMissNum = 0;
        foreach (var nm in nearMissSets)
        {
            // Filter to the subset of nm still active.
            var activeNm = nm.Where(ki => knownToActive.ContainsKey(ki)).ToArray();
            if (activeNm.Length < 3) continue; // can't form a 3-of-4 swap anymore
            nearMissNum++;
            var nmLabel = $"N{nearMissNum}";

            var originalWordsForDisplay = nm.Select(ki => knownWords[ki]).ToArray();
            var nmInActive = activeNm.Select(ki => knownToActive[ki]).ToArray();
            var swaps = new List<(string RemovedWord, double Score, int[] ActiveIdxs)>();

            // For each way to leave one word out, find the best 4th from active\activeNm.
            for (int dropIdx = 0; dropIdx < nmInActive.Length; dropIdx++)
            {
                var triplet = new int[nmInActive.Length - 1];
                int k = 0;
                for (int j = 0; j < nmInActive.Length; j++)
                    if (j != dropIdx) triplet[k++] = nmInActive[j];
                if (triplet.Length != 3) continue;
                var removedWord = activeWords[nmInActive[dropIdx]];

                double existing = activeSim[triplet[0], triplet[1]]
                                + activeSim[triplet[0], triplet[2]]
                                + activeSim[triplet[1], triplet[2]];
                var nmActiveSet = new HashSet<int>(nmInActive);
                int bestFourth = -1;
                double bestScore = double.NegativeInfinity;
                for (int i = 0; i < activeN; i++)
                {
                    if (nmActiveSet.Contains(i)) continue;
                    double added = activeSim[i, triplet[0]] + activeSim[i, triplet[1]] + activeSim[i, triplet[2]];
                    double avg = (existing + added) / 6.0;
                    if (avg > bestScore)
                    {
                        bestScore = avg;
                        bestFourth = i;
                    }
                }
                if (bestFourth < 0) continue;
                var four = new int[] { triplet[0], triplet[1], triplet[2], bestFourth };
                Array.Sort(four);
                swaps.Add((removedWord, bestScore, four));
            }

            swaps.Sort((a, b) => b.Score.CompareTo(a.Score));
            var swapRecords = new List<NearMissSwap>();
            for (int s = 0; s < swaps.Count; s++)
            {
                var (removed, score, fourActive) = swaps[s];
                var replacement = MakeCand(fourActive, score, fourth: null);
                swapRecords.Add(new NearMissSwap(removed, replacement));
                labelMap[$"{nmLabel}s{s + 1}"] = replacement.WordIndices;
            }
            followups.Add(new NearMissFollowup(originalWordsForDisplay, swapRecords));
        }

        var partitions = new List<LabeledPartition>();
        if (activeN >= 8 && activeN % 4 == 0)
        {
            int groupCount = activeN / 4;
            Func<int[], bool>? forbidCheck = forbiddenSetKeys.Count == 0
                ? null
                : group =>
                {
                    if (group.Length != 4) return false;
                    var knownIdxs = new int[4];
                    for (int j = 0; j < 4; j++) knownIdxs[j] = activeKnownIndices[group[j]];
                    Array.Sort(knownIdxs);
                    return forbiddenSetKeys.Contains(CanonicalKey(knownIdxs));
                };
            var rawPartitions = PartitionSearch.EnumerateTopK(activeSim, topPartitions, activeN, groupCount, 4, forbidCheck);
            foreach (var p in rawPartitions)
            {
                var labeled = new CandidateGroup[p.Groups.Length];
                for (int i = 0; i < p.Groups.Length; i++)
                    labeled[i] = MakeCand(p.Groups[i], p.PerGroupAvgSim[i], fourth: null);
                partitions.Add(new LabeledPartition(labeled, p.TotalScore));
            }
        }

        // Phase 3: dense cluster detection. Find groups of 5+ active words that all
        // sit in each other's top-K neighborhood; surface them as warnings with their
        // best 4-subset picks (already reranked by leftover partition).
        var clusterMembers = ClusterDetector.FindDenseClusters(activeN, activeSim, ranked4Tuples);
        var denseClusters = new List<DenseCluster>();
        for (int ci = 0; ci < clusterMembers.Count; ci++)
        {
            var memberSet = new HashSet<int>(clusterMembers[ci]);
            var subsets = reranked4
                .Where(r => r.Indices.All(idx => memberSet.Contains(idx)))
                .Take(5)
                .ToList();
            var subsetCands = new List<CandidateGroup>();
            for (int s = 0; s < subsets.Count; s++)
            {
                var sub = subsets[s];
                var cand = MakeCand(sub.Indices, sub.AvgSim, fourth: null,
                                    leftoverScore: sub.LeftoverPartitionScore);
                subsetCands.Add(cand);
                labelMap[$"X{ci + 1}s{s + 1}"] = cand.WordIndices;
            }
            var members = clusterMembers[ci];
            var clusterWords = members.Select(i => activeWords[i]).ToArray();
            var clusterKnownIdxs = members.Select(i => activeKnownIndices[i]).ToArray();
            denseClusters.Add(new DenseCluster(ci + 1, clusterKnownIdxs, clusterWords, subsetCands));
        }

        // Phase 4: wordplay finder. Looks at active original entries for suffix/prefix
        // wordplay patterns (e.g. BASSOON/BELFAST/NESQUICK/THERMOSTAT all end in ASAP
        // synonyms). Operates on the raw input strings; can include out-of-vocab words.
        var activeWordToKnownIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < activeN; i++)
            activeWordToKnownIdx[activeWords[i]] = activeKnownIndices[i];
        var wordplayFindings = WordplayFinder.Find(activeOriginalEntries, labelCtx);
        var wordplayGroups = new List<WordplayGroup>();
        for (int wi = 0; wi < wordplayFindings.Count; wi++)
        {
            var wp = wordplayFindings[wi];
            var label = $"W{wi + 1}";
            // Register label only if ALL 4 words are in active vocab (lets user say "W1 yes/no").
            var knownIdxs = new List<int>(4);
            foreach (var w in wp.InputWords)
                if (activeWordToKnownIdx.TryGetValue(w, out var k)) knownIdxs.Add(k);
            if (knownIdxs.Count == 4)
            {
                var sorted = knownIdxs.ToArray();
                Array.Sort(sorted);
                labelMap[label] = sorted;
            }
            // Register original entries unconditionally so labels are usable even when
            // one or more input words are out-of-vocabulary.
            labelOriginalMap[label] = wp.InputWords;
            wordplayGroups.Add(new WordplayGroup(wi + 1, wp.InputWords, wp.Affixes, wp.IsSuffix, wp.Tightness));
        }

        // Phase 5: bigram phrase patterns. Detects groups of 4 active entries that share a
        // single common modifier (e.g. all four pair with "revolution" or "jelly ___").
        // Skipped when bigram data isn't loaded.
        var phrasePatternGroups = new List<PhrasePatternGroup>();
        if (bigrams != null)
        {
            var phraseFindings = PhrasePatternFinder.Find(activeOriginalEntries, bigrams);
            for (int pi = 0; pi < phraseFindings.Count; pi++)
            {
                var pf = phraseFindings[pi];
                var label = $"P{pi + 1}";
                var knownIdxs = new List<int>(4);
                foreach (var w in pf.Entries)
                    if (activeWordToKnownIdx.TryGetValue(w, out var k)) knownIdxs.Add(k);
                if (knownIdxs.Count == 4)
                {
                    var sorted = knownIdxs.ToArray();
                    Array.Sort(sorted);
                    labelMap[label] = sorted;
                }
                labelOriginalMap[label] = pf.Entries;
                phrasePatternGroups.Add(new PhrasePatternGroup(
                    pi + 1, pf.Modifier, pf.IsRight, pf.Entries, pf.BigramCounts, pf.Score));
            }
        }

        return new AnalysisResult(anchored4, anchored3, partitions, followups, denseClusters, wordplayGroups, phrasePatternGroups, labelMap, labelOriginalMap);
    }

    /// <summary>
    /// Canonical key for a forbidden 4-set: sorted known-words indices joined by commas.
    /// Caller must pre-sort the input.
    /// </summary>
    public static string CanonicalKey(int[] sortedKnownIndices) => string.Join(",", sortedKnownIndices);

    /// <summary>
    /// Stable key for active-coordinate indices (a candidate's word slots in the current round).
    /// Used as a dictionary key for per-round side data (e.g. leftover-partition scores).
    /// </summary>
    public static string ActiveKey(int[] activeIdxs)
    {
        var sorted = (int[])activeIdxs.Clone();
        Array.Sort(sorted);
        return string.Join(",", sorted);
    }

    private static List<(int[] Indices, double AvgSim)> PickAnchors(
        List<(int[] Indices, double AvgSim)> sortedDesc,
        int maxAnchors)
    {
        var anchors = new List<(int[] Indices, double AvgSim)>();
        var used = new HashSet<int>();
        foreach (var s in sortedDesc)
        {
            bool overlap = false;
            foreach (var idx in s.Indices)
                if (used.Contains(idx)) { overlap = true; break; }
            if (overlap) continue;
            anchors.Add(s);
            foreach (var idx in s.Indices) used.Add(idx);
            if (anchors.Count >= maxAnchors) break;
        }
        return anchors;
    }

    private static List<(int[] Indices, double AvgSim)> PickVariants(
        int[] anchorIndices,
        List<(int[] Indices, double AvgSim)> sortedDesc,
        int maxVariants)
    {
        var anchorSet = new HashSet<int>(anchorIndices);
        int needed = anchorIndices.Length - 1;
        var variants = new List<(int[] Indices, double AvgSim)>(maxVariants);
        foreach (var s in sortedDesc)
        {
            int shared = 0;
            foreach (var idx in s.Indices)
                if (anchorSet.Contains(idx)) shared++;
            if (shared != needed) continue;
            variants.Add(s);
            if (variants.Count >= maxVariants) break;
        }
        return variants;
    }
}
