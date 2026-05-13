namespace DiffViewer.Utility;

/// <summary>
/// Detects whether a blob is binary using git's own heuristic: the presence
/// of any NUL byte in the first 8 KiB.
/// </summary>
internal static class BinaryDetector
{
    private const int ProbeBytes = 8 * 1024;

    public static bool IsBinary(ReadOnlySpan<byte> bytes)
    {
        var probe = bytes.Length > ProbeBytes ? bytes[..ProbeBytes] : bytes;
        return probe.IndexOf((byte)0) >= 0;
    }
}
