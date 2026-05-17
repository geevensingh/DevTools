namespace ConnectionsSolver;

public static class SubsetSearch
{
    /// <summary>
    /// Returns every subset of size <paramref name="size"/> from indices [0, n),
    /// annotated with the average pairwise cosine similarity (read from <paramref name="sim"/>).
    /// </summary>
    public static List<(int[] Indices, double AvgSim)> EnumerateScored(int n, int size, float[,] sim)
    {
        var results = new List<(int[], double)>();
        var current = new int[size];
        Recurse(0, 0, size, n, sim, current, results);
        return results;
    }

    private static void Recurse(int start, int depth, int size, int n, float[,] sim, int[] current, List<(int[], double)> results)
    {
        if (depth == size)
        {
            double sum = 0;
            for (int i = 0; i < size; i++)
                for (int j = i + 1; j < size; j++)
                    sum += sim[current[i], current[j]];
            int pairs = size * (size - 1) / 2;
            results.Add(((int[])current.Clone(), sum / pairs));
            return;
        }

        for (int i = start; i <= n - (size - depth); i++)
        {
            current[depth] = i;
            Recurse(i + 1, depth + 1, size, n, sim, current, results);
        }
    }
}
