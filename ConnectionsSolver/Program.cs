using ConnectionsSolver;
using System.Globalization;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

string? embeddingsPath = null;
string? bigramsPath = null;
bool disableBigrams = false;
int topPartitions = 5;
int anchorCount = 4;
int variantsPerAnchor = 3;
int labelVocabSize = 50_000;
int labelCount = 3;
int fourthCandidates = 3;
double leftoverAlpha = 0.5;
int rerankTopN = 50;
double labelRerankBeta = 1.5;
int labelRerankTopN = 200;
string? inputPath = null;

for (int i = 0; i < args.Length; i++)
{
    var a = args[i];
    switch (a)
    {
        case "--embeddings":
        case "-e":
            embeddingsPath = args[++i];
            break;
        case "--bigrams":
        case "-b":
            bigramsPath = args[++i];
            break;
        case "--no-bigrams":
            disableBigrams = true;
            break;
        case "--top-partitions":
            topPartitions = int.Parse(args[++i]);
            break;
        case "--anchors":
            anchorCount = int.Parse(args[++i]);
            break;
        case "--variants":
            variantsPerAnchor = int.Parse(args[++i]);
            break;
        case "--label-vocab":
            labelVocabSize = int.Parse(args[++i]);
            break;
        case "--labels":
            labelCount = int.Parse(args[++i]);
            break;
        case "--fourth":
            fourthCandidates = int.Parse(args[++i]);
            break;
        case "--rerank-alpha":
            leftoverAlpha = double.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--rerank-top":
            rerankTopN = int.Parse(args[++i]);
            break;
        case "--label-rerank-beta":
            labelRerankBeta = double.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--label-rerank-top":
            labelRerankTopN = int.Parse(args[++i]);
            break;
        case "--no-label-rerank":
            labelRerankBeta = 0;
            break;
        case "--help":
        case "-h":
            PrintUsage();
            return 0;
        default:
            if (a.StartsWith("-")) { Console.Error.WriteLine($"Unknown option: {a}"); return 2; }
            inputPath = a;
            break;
    }
}

embeddingsPath = EmbeddingsResolver.Resolve(embeddingsPath, msg => Console.Error.WriteLine($"  {msg}"));
if (string.IsNullOrEmpty(embeddingsPath))
{
    Console.Error.WriteLine("ERROR: Could not locate a GloVe vector file.");
    Console.Error.WriteLine(EmbeddingsResolver.LookupOrderDescription);
    Console.Error.WriteLine("See README.md for download instructions.");
    return 1;
}

string[] rawWords;
if (inputPath != null)
{
    rawWords = File.ReadAllLines(inputPath);
}
else
{
    Console.Error.WriteLine("Reading 16 words from stdin (one per line; stops automatically after 16)...");
    var lines = new List<string>();
    int validCount = 0;
    string? l;
    while (validCount < 16 && (l = Console.In.ReadLine()) != null)
    {
        lines.Add(l);
        var trimmed = l.Trim();
        if (trimmed.Length > 0 && !trimmed.StartsWith("#"))
            validCount++;
    }
    rawWords = lines.ToArray();
}

var words = rawWords
    .Select(w => w.Trim().ToLowerInvariant())
    .Where(w => w.Length > 0 && !w.StartsWith("#"))
    .Select(w => System.Text.RegularExpressions.Regex.Replace(w, @"\s+", " "))
    .ToArray();

if (words.Length != 16)  
{
    Console.Error.WriteLine($"ERROR: expected exactly 16 non-comment, non-blank words, got {words.Length}.");
    return 1;
}

Console.Error.WriteLine($"Loading GloVe from {embeddingsPath}...");
var sw = System.Diagnostics.Stopwatch.StartNew();
var emb = GloVeEmbeddings.LoadFromFile(embeddingsPath);
sw.Stop();
Console.Error.WriteLine($"  Loaded {emb.VocabularySize:N0} vectors of dim {emb.Dimension} in {sw.Elapsed.TotalSeconds:F1}s");

var knownIndices = new List<int>();
var knownWordsList = new List<string>();
var knownUnitsList = new List<float[]>();
var missing = new List<string>();
var partialEntries = new List<(string Entry, string[] MissingTokens)>();
for (int i = 0; i < 16; i++)
{
    var entry = words[i];
    if (!entry.Contains(' '))
    {
        if (!emb.TryGetVector(entry, out var v))
        {
            missing.Add(entry);
        }
        else
        {
            knownIndices.Add(i);
            knownWordsList.Add(entry);
            knownUnitsList.Add(Similarity.Normalize(v));
        }
    }
    else
    {
        var tokens = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = new List<float[]>();
        var missTok = new List<string>();
        foreach (var t in tokens)
        {
            if (emb.TryGetVector(t, out var v)) found.Add(v);
            else missTok.Add(t);
        }
        if (found.Count == 0)
        {
            missing.Add(entry);
        }
        else
        {
            knownIndices.Add(i);
            knownWordsList.Add(entry);
            knownUnitsList.Add(Similarity.Normalize(Similarity.Centroid(found)));
            if (missTok.Count > 0) partialEntries.Add((entry, missTok.ToArray()));
        }
    }
}

if (missing.Count > 0)
{
    Console.Error.WriteLine($"WARNING: {missing.Count} entry/entries not in GloVe vocabulary; proceeding with the other {knownWordsList.Count}:");
    foreach (var m in missing) Console.Error.WriteLine($"  - {m}");
}
if (partialEntries.Count > 0)
{
    Console.Error.WriteLine($"NOTE: {partialEntries.Count} multi-word entry/entries had some out-of-vocab token(s); used average of the in-vocab tokens:");
    foreach (var (entry, miss) in partialEntries)
        Console.Error.WriteLine($"  - \"{entry}\"  (skipped: {string.Join(", ", miss)})");
}

if (knownWordsList.Count < 3)
{
    Console.Error.WriteLine($"ERROR: only {knownWordsList.Count} word(s) recognized; need at least 3 to suggest a group.");
    return 1;
}

int n = knownWordsList.Count;
var knownWords = knownWordsList.ToArray();
var knownToOriginal = knownIndices.ToArray();
var unitVectors = knownUnitsList.ToArray();

Console.Error.WriteLine($"Preparing label vocabulary (top {labelVocabSize:N0} words)...");
sw.Restart();
var labelCtx = new LabelingContext(emb, labelVocabSize);
sw.Stop();
Console.Error.WriteLine($"  Done in {sw.Elapsed.TotalSeconds:F2}s");

BigramData? bigrams = null;
if (disableBigrams)
{
    Console.Error.WriteLine("Bigram phrase-pattern detection disabled (--no-bigrams).");
}
else
{
    var resolvedBigrams = BigramResolver.Resolve(bigramsPath, msg => Console.Error.WriteLine($"  {msg}"));
    if (string.IsNullOrEmpty(resolvedBigrams))
    {
        Console.Error.WriteLine("WARNING: bigram counts file not found; phrase-pattern detection (P labels) disabled.");
        Console.Error.WriteLine("  " + BigramResolver.LookupOrderDescription);
        Console.Error.WriteLine("  Download https://norvig.com/ngrams/count_2w.txt to enable.");
    }
    else
    {
        Console.Error.WriteLine($"Loading bigram counts from {resolvedBigrams}...");
        sw.Restart();
        try
        {
            bigrams = BigramData.LoadFromFile(resolvedBigrams);
            sw.Stop();
            Console.Error.WriteLine($"  Loaded {bigrams.VocabSize:N0} unique anchor words in {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.Error.WriteLine($"WARNING: failed to load bigram file ({ex.Message}); phrase-pattern detection disabled.");
            bigrams = null;
        }
    }
}

InteractiveSession.Run(
    words, knownWords, knownToOriginal, missing, unitVectors, labelCtx, bigrams,
    new InteractiveSession.Options(
        AnchorCount: anchorCount,
        VariantsPerAnchor: variantsPerAnchor,
        TopPartitions: topPartitions,
        LabelCount: labelCount,
        FourthCandidates: fourthCandidates,
        LeftoverAlpha: leftoverAlpha,
        RerankTopN: rerankTopN,
        LabelRerankBeta: labelRerankBeta,
        LabelRerankTopN: labelRerankTopN));
return 0;

static void PrintUsage()
{
    Console.WriteLine("ConnectionsSolver - semantic-similarity helper for NYT Connections puzzles");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  ConnectionsSolver [options] [<input-file>]");
    Console.WriteLine();
    Console.WriteLine("If <input-file> is omitted, reads 16 words (one per line) from stdin.");
    Console.WriteLine("Lines starting with '#' and blank lines are ignored.");
    Console.WriteLine();
    Console.WriteLine("After loading, runs an interactive loop. Each round prints labeled suggestions");
    Console.WriteLine("(A1..A4 size-4 anchors, A1v1..A1v3 single-word-swap variants, B1.. size-3");
    Console.WriteLine("anchors, N1s1.. near-miss follow-ups), then prompts for feedback:");
    Console.WriteLine("    <label or 4 words> <yes | no | off-by-one>");
    Console.WriteLine("'yes' removes the words from play; 'no' forbids that exact 4-set; 'off-by-one'");
    Console.WriteLine("forbids it AND tracks it so future rounds offer the four possible single-word");
    Console.WriteLine("swaps as labeled N* options.");
    Console.WriteLine();
    Console.WriteLine("At the same prompt:");
    Console.WriteLine("    with <1-3 entries>   asks 'what completes this set?' and shows top 5");
    Console.WriteLine("                          ranked 4-sets containing those words (labeled C1..C5).");
    Console.WriteLine("                          Multi-word entries comma-separated, e.g.");
    Console.WriteLine("                              with hero, hoagie, sub");
    Console.WriteLine("                              with free love, hippie");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -e, --embeddings <path>   Path to GloVe text file (or set GLOVE_PATH).");
    Console.WriteLine("                              If neither is supplied, falls back to");
    Console.WriteLine("                              <Downloads>\\glove.6B\\glove.6B.300d.txt, then");
    Console.WriteLine("                              auto-extracts <Downloads>\\glove.6B.zip if present.");
    Console.WriteLine("  -b, --bigrams <path>      Path to Norvig-format bigram counts (or set BIGRAMS_PATH).");
    Console.WriteLine("                              Falls back to <Downloads>\\count_2w.txt. Optional;");
    Console.WriteLine("                              when missing, [P] phrase-pattern detection is disabled.");
    Console.WriteLine("      --no-bigrams          Skip bigram loading even if a file is present.");
    Console.WriteLine("      --anchors N           Disjoint top groups to show (default 4)");
    Console.WriteLine("      --variants N          Single-word-swap variants per anchor (default 3)");
    Console.WriteLine("      --top-partitions N    How many full partitions (default 5)");
    Console.WriteLine("      --label-vocab N       Label search vocabulary size (default 50000)");
    Console.WriteLine("      --labels N            Labels per group (default 3)");
    Console.WriteLine("      --fourth N            Possible-4th-word suggestions per triplet (default 3)");
    Console.WriteLine("      --rerank-alpha F      Phase 2 leftover-partition weight (default 0.5)");
    Console.WriteLine("      --rerank-top N        Phase 2 top-N candidates to rerank (default 50)");
    Console.WriteLine("      --label-rerank-beta F Phase 6 centroid-to-label weight (default 1.5, 0 disables)");
    Console.WriteLine("      --label-rerank-top N  Phase 6 top-N candidates to rerank (default 200)");
    Console.WriteLine("      --no-label-rerank     Shortcut for --label-rerank-beta 0");
    Console.WriteLine("  -h, --help                Show this help");
}
