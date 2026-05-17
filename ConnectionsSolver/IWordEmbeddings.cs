namespace ConnectionsSolver;

public interface IWordEmbeddings
{
    int Dimension { get; }
    int VocabularySize { get; }

    bool TryGetVector(string word, out float[] vector);

    IEnumerable<(string Word, float[] Vector)> MostFrequent(int topN);
}
