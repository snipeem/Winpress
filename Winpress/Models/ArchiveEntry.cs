using System.IO;
using Winpress.Helpers;

namespace Winpress.Models;

/// <summary>
/// Represents a single file or folder inside an open archive.
/// </summary>
public class ArchiveEntry
{
    // ── Identity ─────────────────────────────────────────────────────────────
    /// <summary>File or folder name (last path component only).</summary>
    public string Name       { get; init; } = string.Empty;

    /// <summary>Full slash-separated path inside the archive, e.g. "folder/sub/file.txt".</summary>
    public string FullPath   { get; init; } = string.Empty;

    // ── Type flags ───────────────────────────────────────────────────────────
    public bool IsDirectory  { get; init; }

    /// <summary>True when this entry is a synthesised folder (no real directory entry in the archive).</summary>
    public bool IsVirtual    { get; init; }

    /// <summary>True when the entry itself is an archive — enables drilling into nested archives.</summary>
    public bool IsNestedArchive => !IsDirectory && FormatDetector.IsArchiveExtension(Extension);

    // ── Sizes & metadata ─────────────────────────────────────────────────────
    public long     Size         { get; init; }
    public long     CompressedSize { get; init; }
    public DateTime? LastModified { get; init; }
    public bool     IsEncrypted  { get; init; }

    // ── Convenience display strings (used by ListView bindings) ───────────────
    public string SizeDisplay =>
        IsDirectory ? string.Empty : FileSizeHelper.Format(Size);

    public string CompressedSizeDisplay =>
        IsDirectory ? string.Empty : FileSizeHelper.Format(CompressedSize);

    public string RatioDisplay =>
        (IsDirectory || Size <= 0) ? string.Empty
        : $"{(1.0 - (double)CompressedSize / Size) * 100:F1}%";

    public string ModifiedDisplay =>
        LastModified.HasValue ? LastModified.Value.ToString("yyyy-MM-dd  HH:mm") : string.Empty;

    public string TypeDisplay =>
        IsDirectory ? "Folder"
        : string.IsNullOrEmpty(Extension) ? "File"
        : $"{Extension.TrimStart('.').ToUpperInvariant()} File";

    /// <summary>Lower-case extension, including the dot, e.g. ".zip".</summary>
    public string Extension =>
        IsDirectory ? string.Empty : Path.GetExtension(Name).ToLowerInvariant();

    // ── Icon glyph (Segoe MDL2 Assets / Segoe Fluent Icons) ──────────────────
    /// <summary>
    /// Unicode character from the Segoe MDL2 Assets font used as the row icon.
    /// This avoids any external icon library dependency.
    /// </summary>
    public string IconGlyph =>
        IsDirectory     ? "\uE8B7"   // folder
        : IsNestedArchive ? "\uEC50"   // archive-in-archive  (package)
        : GetFileGlyph(Extension);

    private static string GetFileGlyph(string ext) => ext switch
    {
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" or ".xz"
            or ".lz4" or ".zst" or ".cab" or ".iso" or ".wim"
            or ".deb" or ".rpm" or ".cpio" or ".ar"  => "\uEC50",  // package
        ".exe" or ".msi" or ".msix"                  => "\uE756",  // application
        ".pdf"                                        => "\uEA90",  // PDF
        ".jpg" or ".jpeg" or ".png" or ".gif"
            or ".bmp" or ".tiff" or ".webp" or ".svg" => "\uEB9F",  // image
        ".mp3" or ".flac" or ".wav" or ".aac"
            or ".ogg" or ".m4a"                      => "\uE8D6",  // music
        ".mp4" or ".mkv" or ".avi" or ".mov"
            or ".wmv" or ".flv"                      => "\uE714",  // video
        ".txt" or ".log" or ".md" or ".rst"          => "\uE8A5",  // document
        ".doc" or ".docx" or ".odt" or ".rtf"        => "\uE8A5",
        ".xls" or ".xlsx" or ".ods" or ".csv"        => "\uE8A5",
        ".cs" or ".py" or ".js" or ".ts" or ".java"
            or ".cpp" or ".c" or ".h" or ".go"
            or ".rs" or ".rb" or ".php"              => "\uE943",  // code
        ".json" or ".xml" or ".yaml" or ".toml"
            or ".ini" or ".cfg" or ".conf"           => "\uE943",
        _                                            => "\uE8A5",  // generic file
    };
}
