using System.Text;

namespace DiffViewer.Models;

/// <summary>
/// Decoded contents of one side of a diff. Carries the bytes, the detected
/// encoding, and the decoded string so the renderer and the unified-diff
/// formatter can both work without re-decoding.
/// </summary>
/// <param name="Bytes">Raw post-filter bytes (clean/smudge already applied).</param>
/// <param name="Encoding">The encoding the bytes were decoded with.</param>
/// <param name="Text">Decoded text. Empty string if <see cref="Bytes"/> is empty.</param>
/// <param name="IsBinary">True if NUL-byte heuristic flagged the content as binary.</param>
/// <param name="IsLfsPointer">True if the content matches the Git LFS pointer signature.</param>
public sealed record BlobContent(
    byte[] Bytes,
    Encoding Encoding,
    string Text,
    bool IsBinary,
    bool IsLfsPointer)
{
    public static BlobContent Empty { get; } =
        new BlobContent(Array.Empty<byte>(), Encoding.UTF8, string.Empty, false, false);
}
