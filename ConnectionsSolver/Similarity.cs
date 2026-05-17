namespace ConnectionsSolver;

public static class Similarity
{
    public static float Magnitude(float[] v)
    {
        double s = 0;
        for (int i = 0; i < v.Length; i++) s += v[i] * v[i];
        return (float)System.Math.Sqrt(s);
    }

    public static float[] Normalize(float[] v)
    {
        var mag = Magnitude(v);
        if (mag < 1e-9f) return (float[])v.Clone();
        var r = new float[v.Length];
        for (int i = 0; i < v.Length; i++) r[i] = v[i] / mag;
        return r;
    }

    public static float Dot(float[] a, float[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return (float)s;
    }

    public static float Cosine(float[] a, float[] b)
    {
        return Dot(a, b) / (Magnitude(a) * Magnitude(b));
    }

    public static float[] Centroid(IReadOnlyList<float[]> vectors)
    {
        if (vectors.Count == 0) throw new ArgumentException("Need at least one vector.");
        int d = vectors[0].Length;
        var c = new float[d];
        foreach (var v in vectors)
            for (int i = 0; i < d; i++) c[i] += v[i];
        for (int i = 0; i < d; i++) c[i] /= vectors.Count;
        return c;
    }

    /// <summary>
    /// Builds an NxN cosine-similarity matrix. Expects pre-normalized (unit-length) vectors.
    /// </summary>
    public static float[,] BuildPairwiseMatrix(float[][] unitVectors)
    {
        int n = unitVectors.Length;
        var m = new float[n, n];
        for (int i = 0; i < n; i++)
        {
            m[i, i] = 1f;
            for (int j = i + 1; j < n; j++)
            {
                var v = Dot(unitVectors[i], unitVectors[j]);
                m[i, j] = v;
                m[j, i] = v;
            }
        }
        return m;
    }
}
