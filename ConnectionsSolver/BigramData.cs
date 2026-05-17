using System.Globalization;
using System.Text;

namespace ConnectionsSolver;

/// <summary>
/// Loads and queries a Norvig-format word-bigram counts file
/// (https://norvig.com/ngrams/count_2w.txt). Format: each line has
/// "word1 word2\tcount". On first load, also writes a sibling .cache binary
/// file for fast subsequent loads (same pattern as <see cref="GloVeEmbeddings"/>).
/// </summary>
public sealed class BigramData
{
    private const uint CacheMagic = 0x43474942u; // "BIGC"
    private const int CacheVersion = 1;

    // For each w, list of (rightWord, count) sorted by count desc.
    private readonly Dictionary<string, (string Word, long Count)[]> _next;
    // For each w, list of (leftWord, count) sorted by count desc.
    private readonly Dictionary<string, (string Word, long Count)[]> _prev;

    public int VocabSize => _next.Count;
    public long TotalBigramOccurrences { get; }

    private BigramData(
        Dictionary<string, (string, long)[]> next,
        Dictionary<string, (string, long)[]> prev,
        long totalOccurrences)
    {
        _next = next;
        _prev = prev;
        TotalBigramOccurrences = totalOccurrences;
    }

    /// <summary>Top K words that commonly FOLLOW <paramref name="word"/>, sorted by count desc.</summary>
    public IReadOnlyList<(string Word, long Count)> GetNextWords(string word, int topK)
    {
        if (!_next.TryGetValue(word.ToLowerInvariant(), out var arr)) return Array.Empty<(string, long)>();
        return arr.Length <= topK ? arr : arr[..topK];
    }

    /// <summary>Top K words that commonly PRECEDE <paramref name="word"/>, sorted by count desc.</summary>
    public IReadOnlyList<(string Word, long Count)> GetPrevWords(string word, int topK)
    {
        if (!_prev.TryGetValue(word.ToLowerInvariant(), out var arr)) return Array.Empty<(string, long)>();
        return arr.Length <= topK ? arr : arr[..topK];
    }

    public static BigramData LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Bigram file not found: {path}");

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
                Console.Error.WriteLine($"Warning: failed to load bigram cache ({ex.Message}); rebuilding from text.");
            }
        }

        var result = LoadFromTextFile(path);

        try { WriteBinaryCache(result, cachePath); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: could not write bigram cache to {cachePath}: {ex.Message}");
        }

        return result;
    }

    private static BigramData LoadFromTextFile(string path)
    {
        var nextMut = new Dictionary<string, List<(string, long)>>(StringComparer.Ordinal);
        var prevMut = new Dictionary<string, List<(string, long)>>(StringComparer.Ordinal);
        long total = 0;

        using var reader = new StreamReader(path, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            int tabIdx = line.IndexOf('\t');
            if (tabIdx < 0) continue;

            var pair = line.AsSpan(0, tabIdx);
            var countSpan = line.AsSpan(tabIdx + 1);

            if (!long.TryParse(countSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                continue;

            int spIdx = pair.IndexOf(' ');
            if (spIdx <= 0 || spIdx >= pair.Length - 1) continue;

            string w1 = pair.Slice(0, spIdx).ToString().ToLowerInvariant();
            string w2 = pair.Slice(spIdx + 1).ToString().ToLowerInvariant();
            if (w1.Length == 0 || w2.Length == 0) continue;

            if (!nextMut.TryGetValue(w1, out var nList)) { nList = new(); nextMut[w1] = nList; }
            nList.Add((w2, count));

            if (!prevMut.TryGetValue(w2, out var pList)) { pList = new(); prevMut[w2] = pList; }
            pList.Add((w1, count));

            total += count;
        }

        // Freeze: sort each list by count desc, convert to array for cheap top-K slices.
        var next = new Dictionary<string, (string, long)[]>(nextMut.Count, StringComparer.Ordinal);
        foreach (var kv in nextMut)
        {
            kv.Value.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            next[kv.Key] = kv.Value.ToArray();
        }
        var prev = new Dictionary<string, (string, long)[]>(prevMut.Count, StringComparer.Ordinal);
        foreach (var kv in prevMut)
        {
            kv.Value.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            prev[kv.Key] = kv.Value.ToArray();
        }

        return new BigramData(next, prev, total);
    }

    private static BigramData LoadFromBinaryCache(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);

        if (br.ReadUInt32() != CacheMagic) throw new InvalidDataException("Bad cache magic.");
        if (br.ReadInt32() != CacheVersion) throw new InvalidDataException("Unsupported cache version.");

        long totalOccurrences = br.ReadInt64();
        var next = ReadMap(br);
        var prev = ReadMap(br);
        return new BigramData(next, prev, totalOccurrences);

        static Dictionary<string, (string, long)[]> ReadMap(BinaryReader r)
        {
            int n = r.ReadInt32();
            var d = new Dictionary<string, (string, long)[]>(n, StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                string key = r.ReadString();
                int m = r.ReadInt32();
                var arr = new (string, long)[m];
                for (int j = 0; j < m; j++)
                {
                    string w = r.ReadString();
                    long c = r.ReadInt64();
                    arr[j] = (w, c);
                }
                d[key] = arr;
            }
            return d;
        }
    }

    private static void WriteBinaryCache(BigramData b, string path)
    {
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false))
        {
            bw.Write(CacheMagic);
            bw.Write(CacheVersion);
            bw.Write(b.TotalBigramOccurrences);
            WriteMap(bw, b._next);
            WriteMap(bw, b._prev);
        }
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);

        static void WriteMap(BinaryWriter w, Dictionary<string, (string, long)[]> d)
        {
            w.Write(d.Count);
            foreach (var kv in d)
            {
                w.Write(kv.Key);
                w.Write(kv.Value.Length);
                foreach (var (word, count) in kv.Value)
                {
                    w.Write(word);
                    w.Write(count);
                }
            }
        }
    }
}
