using System.Globalization;
using System.Text;

namespace ConnectionsSolver;

public sealed class GloVeEmbeddings : IWordEmbeddings
{
    private const uint CacheMagic = 0x43424C47u; // "GLBC"
    private const int CacheVersion = 1;

    private readonly Dictionary<string, int> _wordToIndex;
    private readonly string[] _words;
    private readonly float[][] _vectors;

    public int Dimension { get; }
    public int VocabularySize => _words.Length;

    private GloVeEmbeddings(Dictionary<string, int> w2i, string[] words, float[][] vectors, int dim)
    {
        _wordToIndex = w2i;
        _words = words;
        _vectors = vectors;
        Dimension = dim;
    }

    public bool TryGetVector(string word, out float[] vector)
    {
        if (_wordToIndex.TryGetValue(word.ToLowerInvariant(), out var idx))
        {
            vector = _vectors[idx];
            return true;
        }
        vector = Array.Empty<float>();
        return false;
    }

    public IEnumerable<(string Word, float[] Vector)> MostFrequent(int topN)
    {
        int n = System.Math.Min(topN, _words.Length);
        for (int i = 0; i < n; i++)
            yield return (_words[i], _vectors[i]);
    }

    /// <summary>
    /// Loads a GloVe text file. The first time, the result is also written to a sibling
    /// .cache binary file so subsequent loads are fast. The cache is invalidated whenever
    /// the source text file is newer.
    /// </summary>
    public static GloVeEmbeddings LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"GloVe file not found: {path}");

        string cachePath = path + ".cache";
        if (File.Exists(cachePath) &&
            File.GetLastWriteTimeUtc(cachePath) >= File.GetLastWriteTimeUtc(path))
        {
            try
            {
                return LoadFromBinaryCache(cachePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: failed to load cache ({ex.Message}); rebuilding from text.");
            }
        }

        var result = LoadFromTextFile(path);

        try
        {
            WriteBinaryCache(result, cachePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: could not write cache to {cachePath}: {ex.Message}");
        }

        return result;
    }

    private static GloVeEmbeddings LoadFromTextFile(string path)
    {
        var words = new List<string>(500_000);
        var vectors = new List<float[]>(500_000);
        int dim = 0;

        using var reader = new StreamReader(path, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            int firstSpace = line.IndexOf(' ');
            if (firstSpace <= 0) continue;

            string word = line.Substring(0, firstSpace);
            var rest = line.AsSpan(firstSpace + 1);

            if (dim == 0)
            {
                int count = 1;
                for (int i = 0; i < rest.Length; i++)
                    if (rest[i] == ' ') count++;
                dim = count;
            }

            var vec = new float[dim];
            int writeIdx = 0;
            int start = 0;
            for (int i = 0; i <= rest.Length; i++)
            {
                if (i == rest.Length || rest[i] == ' ')
                {
                    if (writeIdx >= dim) break;
                    var slice = rest.Slice(start, i - start);
                    vec[writeIdx++] = float.Parse(slice, NumberStyles.Float, CultureInfo.InvariantCulture);
                    start = i + 1;
                }
            }
            if (writeIdx != dim) continue;

            words.Add(word);
            vectors.Add(vec);
        }

        var w2i = new Dictionary<string, int>(words.Count);
        for (int i = 0; i < words.Count; i++)
            w2i[words[i]] = i;

        return new GloVeEmbeddings(w2i, words.ToArray(), vectors.ToArray(), dim);
    }

    private static GloVeEmbeddings LoadFromBinaryCache(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);

        uint magic = br.ReadUInt32();
        if (magic != CacheMagic) throw new InvalidDataException("Bad cache magic.");
        int version = br.ReadInt32();
        if (version != CacheVersion) throw new InvalidDataException($"Unsupported cache version: {version}");

        int vocab = br.ReadInt32();
        int dim = br.ReadInt32();
        if (vocab < 0 || dim <= 0) throw new InvalidDataException("Bad cache header.");

        var words = new string[vocab];
        var vectors = new float[vocab][];
        var w2i = new Dictionary<string, int>(vocab);

        int vecByteLen = dim * sizeof(float);
        var byteBuf = new byte[vecByteLen];

        for (int i = 0; i < vocab; i++)
        {
            ushort wlen = br.ReadUInt16();
            var wbytes = br.ReadBytes(wlen);
            string word = Encoding.UTF8.GetString(wbytes);

            int read = 0;
            while (read < vecByteLen)
            {
                int n = fs.Read(byteBuf, read, vecByteLen - read);
                if (n == 0) throw new EndOfStreamException();
                read += n;
            }

            var vec = new float[dim];
            Buffer.BlockCopy(byteBuf, 0, vec, 0, vecByteLen);

            words[i] = word;
            vectors[i] = vec;
            w2i[word] = i;
        }

        return new GloVeEmbeddings(w2i, words, vectors, dim);
    }

    private static void WriteBinaryCache(GloVeEmbeddings emb, string path)
    {
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false))
        {
            bw.Write(CacheMagic);
            bw.Write(CacheVersion);
            bw.Write(emb.VocabularySize);
            bw.Write(emb.Dimension);

            int vecByteLen = emb.Dimension * sizeof(float);
            var byteBuf = new byte[vecByteLen];

            for (int i = 0; i < emb._words.Length; i++)
            {
                var wbytes = Encoding.UTF8.GetBytes(emb._words[i]);
                if (wbytes.Length > ushort.MaxValue)
                    throw new InvalidDataException($"Word too long for cache format: {emb._words[i]}");

                bw.Write((ushort)wbytes.Length);
                bw.Write(wbytes);

                Buffer.BlockCopy(emb._vectors[i], 0, byteBuf, 0, vecByteLen);
                bw.Write(byteBuf);
            }
        }
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}
