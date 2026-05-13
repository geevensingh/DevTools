using System.Text;

namespace DiffViewer.Utility;

/// <summary>
/// Detects the encoding of a byte buffer using BOM sniffing first, then a
/// strict UTF-8 decode probe, falling back to Windows-1252 (which can
/// decode any byte sequence). Mirrors Resolved Design Decision #11 in the
/// plan: BOM ⟶ strict UTF-8 ⟶ Windows-1252.
/// </summary>
internal static class EncodingDetector
{
    static EncodingDetector()
    {
        // Windows-1252 lives in CodePagesEncodingProvider on .NET 5+;
        // safe to call repeatedly.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly byte[] Utf16LeBom = { 0xFF, 0xFE };
    private static readonly byte[] Utf16BeBom = { 0xFE, 0xFF };
    private static readonly byte[] Utf32LeBom = { 0xFF, 0xFE, 0x00, 0x00 };
    private static readonly byte[] Utf32BeBom = { 0x00, 0x00, 0xFE, 0xFF };

    public static Encoding Detect(ReadOnlySpan<byte> bytes)
    {
        // UTF-32 BOMs must come before UTF-16 LE (FF FE [00 00 ...]).
        if (StartsWith(bytes, Utf32LeBom)) return Encoding.UTF32;
        if (StartsWith(bytes, Utf32BeBom)) return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        if (StartsWith(bytes, Utf8Bom)) return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        if (StartsWith(bytes, Utf16LeBom)) return Encoding.Unicode;
        if (StartsWith(bytes, Utf16BeBom)) return Encoding.BigEndianUnicode;

        if (IsValidUtf8(bytes))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        }

        // Windows-1252 - the lossless fallback. Always succeeds.
        return Encoding.GetEncoding("Windows-1252");
    }

    public static string Decode(ReadOnlySpan<byte> bytes, out Encoding encoding)
    {
        encoding = Detect(bytes);

        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        // Strip BOM bytes from the prefix to avoid embedding U+FEFF in the decoded string.
        int bomLength = encoding switch
        {
            UTF32Encoding when StartsWith(bytes, Utf32LeBom) => Utf32LeBom.Length,
            UTF32Encoding when StartsWith(bytes, Utf32BeBom) => Utf32BeBom.Length,
            UTF8Encoding u8 when u8.GetPreamble().Length > 0 && StartsWith(bytes, Utf8Bom) => Utf8Bom.Length,
            _ when ReferenceEquals(encoding, Encoding.Unicode) && StartsWith(bytes, Utf16LeBom) => Utf16LeBom.Length,
            _ when ReferenceEquals(encoding, Encoding.BigEndianUnicode) && StartsWith(bytes, Utf16BeBom) => Utf16BeBom.Length,
            _ => 0,
        };

        return encoding.GetString(bytes[bomLength..]);
    }

    private static bool StartsWith(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> prefix) =>
        bytes.Length >= prefix.Length && bytes[..prefix.Length].SequenceEqual(prefix);

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        // Strict UTF-8 decode probe: any invalid sequence throws.
        try
        {
            var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            // GetCharCount is enough - it walks the bytes and throws on invalid ones.
            _ = enc.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
