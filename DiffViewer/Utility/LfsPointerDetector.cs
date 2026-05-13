using System.Text;

namespace DiffViewer.Utility;

/// <summary>
/// Detects whether a blob is an unfetched Git LFS pointer. Pointers are
/// short (~130 bytes) UTF-8 text files starting with the literal version
/// line <c>version https://git-lfs.github.com/spec/v1</c>.
/// </summary>
internal static class LfsPointerDetector
{
    // Conservative upper bound; real pointers are ~130 bytes.
    private const int MaxPointerSize = 1024;

    private static readonly byte[] SignatureUtf8 =
        Encoding.UTF8.GetBytes("version https://git-lfs.github.com/spec/v1");

    public static bool IsLfsPointer(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxPointerSize)
        {
            return false;
        }

        if (bytes.Length < SignatureUtf8.Length)
        {
            return false;
        }

        return bytes[..SignatureUtf8.Length].SequenceEqual(SignatureUtf8);
    }
}
