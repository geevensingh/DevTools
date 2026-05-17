namespace ConnectionsSolver;

public sealed record CandidateGroup(
    int[] WordIndices,
    string[] Words,
    double AverageSimilarity,
    string[] Labels)
{
    public int Size => WordIndices.Length;

    /// <summary>
    /// Only populated for size-3 candidates: the most plausible 4th words from the
    /// remaining 13 input words, each scored as if added to this group.
    /// </summary>
    public IReadOnlyList<(string Word, double AverageSimilarity)>? CandidateFourth { get; init; }

    /// <summary>
    /// Optional: avg per-group score of the best partition of the remaining active words
    /// when this candidate is fixed. Higher → this group "leaves the rest of the puzzle
    /// in good shape." Populated by partition-score reranking for 4-word candidates.
    /// </summary>
    public double? LeftoverPartitionScore { get; init; }
}

public sealed record LabeledPartition(CandidateGroup[] Groups, double TotalScore);

public sealed record AnchoredGroup(CandidateGroup Anchor, IReadOnlyList<CandidateGroup> Variants);
