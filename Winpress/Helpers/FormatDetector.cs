using System.IO;

namespace Winpress.Helpers;

/// <summary>
/// Detects archive formats both by file extension and by reading the file's
/// magic bytes (signature).  Magic-byte detection is more reliable because it
/// works even when the extension is wrong or absent.
/// </summary>
public static class FormatDetector
{
    // ── Known archive extensions ──────────────────────────────────────────────
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // General archives
        ".zip", ".zipx", ".7z", ".rar", ".tar", ".tgz", ".tbz", ".tbz2", ".txz",
        ".tlz4", ".taz", ".cab",
        // Compression
        ".gz", ".bz2", ".xz", ".lz4", ".zst", ".lzma", ".lz",
        // Disk images
        ".iso", ".wim", ".img", ".dmg",
        // Package formats
        ".deb", ".rpm", ".apk", ".pkg", ".msi", ".msix", ".appx",
        // Specialty
        ".cpio", ".ar", ".a", ".arj", ".lha", ".lzh",
        // Self-extracting
        ".exe"  // Note: not all .exe are archives; ArchiveService validates by magic bytes
    };

    /// <summary>Returns true when the extension alone suggests the file could be an archive.</summary>
    public static bool IsArchiveExtension(string extension) =>
        ArchiveExtensions.Contains(extension);

    // ── Magic-byte signatures ─────────────────────────────────────────────────
    // Each entry: (offset, bytes)
    private static readonly (int Offset, byte[] Magic, string Format)[] Signatures =
    {
        (0,  new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "zip"),        // ZIP local file header
        (0,  new byte[] { 0x50, 0x4B, 0x05, 0x06 }, "zip"),        // ZIP empty archive
        (0,  new byte[] { 0x50, 0x4B, 0x07, 0x08 }, "zip"),        // ZIP spanned
        (0,  new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }, "7z"),  // 7-Zip
        (0,  new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 }, "rar4"), // RAR 4.x
        (0,  new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 }, "rar5"), // RAR 5+
        (0,  new byte[] { 0x1F, 0x8B }, "gz"),                     // GZip
        (0,  new byte[] { 0x42, 0x5A, 0x68 }, "bz2"),              // BZip2
        (0,  new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 }, "xz"), // XZ
        (0,  new byte[] { 0x04, 0x22, 0x4D, 0x18 }, "lz4"),        // LZ4 legacy
        (0,  new byte[] { 0x28, 0xB5, 0x2F, 0xFD }, "zst"),        // Zstandard
        (257,new byte[] { 0x75, 0x73, 0x74, 0x61, 0x72 }, "tar"),  // TAR (POSIX ustar magic)
        (0,  new byte[] { 0x43, 0x44, 0x30, 0x30, 0x31 }, "iso"), // ISO 9660
        (0,  new byte[] { 0x4D, 0x53, 0x57, 0x49, 0x4D, 0x00, 0x00, 0x00 }, "wim"), // WIM
        (0,  new byte[] { 0x4D, 0x5A }, "exe"),                    // MZ header (potential SFX)
        (0,  new byte[] { 0x21, 0x3C, 0x61, 0x72, 0x63, 0x68, 0x3E }, "ar"), // AR / DEB
    };

    /// <summary>
    /// Reads the first 512 bytes of a file and checks against known signatures.
    /// Returns the format identifier string, or "unknown".
    /// </summary>
    public static string DetectFormat(string filePath)
    {
        try
        {
            // Read enough bytes to cover all signatures (TAR magic is at offset 257)
            var header = new byte[512];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var read = fs.Read(header, 0, header.Length);

            foreach (var (offset, magic, format) in Signatures)
            {
                if (offset + magic.Length > read) continue;
                var match = true;
                for (var i = 0; i < magic.Length; i++)
                {
                    if (header[offset + i] != magic[i]) { match = false; break; }
                }
                if (match) return format;
            }
        }
        catch { /* file not readable — fall back to extension */ }

        return "unknown";
    }

    /// <summary>
    /// Returns true when the file at <paramref name="filePath"/> is an archive,
    /// using both magic-byte detection and extension as fallbacks.
    /// </summary>
    public static bool IsArchiveFile(string filePath)
    {
        var format = DetectFormat(filePath);
        if (format != "unknown") return true;
        var ext = Path.GetExtension(filePath);
        return IsArchiveExtension(ext);
    }

    /// <summary>
    /// Human-readable name for the detected format string.
    /// </summary>
    public static string GetFormatName(string format) => format switch
    {
        "zip"  => "ZIP Archive",
        "7z"   => "7-Zip Archive",
        "rar4" => "RAR Archive (v4)",
        "rar5" => "RAR Archive (v5+)",
        "gz"   => "GZip Compressed",
        "bz2"  => "BZip2 Compressed",
        "xz"   => "XZ Compressed",
        "lz4"  => "LZ4 Compressed",
        "zst"  => "Zstandard Compressed",
        "tar"  => "TAR Archive",
        "iso"  => "ISO Disc Image",
        "wim"  => "Windows Image",
        "ar"   => "AR / Debian Package",
        "exe"  => "Self-Extracting Archive",
        _      => "Archive"
    };
}
