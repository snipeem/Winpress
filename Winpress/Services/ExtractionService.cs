using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using Winpress.Models;

namespace Winpress.Services;

/// <summary>Progress information emitted during extraction.</summary>
public record ExtractionProgress(
    string CurrentFile,
    int    FilesCompleted,
    int    TotalFiles,
    long   BytesCompleted,
    long   TotalBytes
)
{
    public double Fraction => TotalBytes > 0 ? Math.Min(1.0, (double)BytesCompleted / TotalBytes) : 0;
}

/// <summary>
/// Extracts archive contents to a destination directory.
/// Supports full extraction, selective extraction, and single-file extraction
/// to a temp folder (for preview / open-with).
/// </summary>
public class ExtractionService
{
    // ── Full archive extraction ───────────────────────────────────────────────

    /// <summary>Extracts every entry in the archive to <paramref name="destination"/>.</summary>
    public async Task ExtractAllAsync(
        string archivePath,
        string destination,
        string? password               = null,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken ct           = default)
    {
        await Task.Run(() => DoExtract(archivePath, destination, password: password,
            filter: null, progress: progress, ct: ct), ct);
    }

    // ── Selective extraction ──────────────────────────────────────────────────

    /// <summary>
    /// Extracts only the <paramref name="selected"/> entries (and their children)
    /// to <paramref name="destination"/>.
    /// </summary>
    public async Task ExtractSelectedAsync(
        string archivePath,
        IEnumerable<ArchiveEntry> selected,
        string destination,
        string? password               = null,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken ct           = default)
    {
        // Build a set of path prefixes to include (entry + all descendants)
        var prefixes = selected
            .Select(e => e.FullPath + (e.IsDirectory ? "/" : string.Empty))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await Task.Run(() => DoExtract(archivePath, destination, password: password,
            filter: key =>
            {
                var normalised = (key ?? string.Empty).Replace('\\', '/');
                return prefixes.Any(p =>
                    normalised.StartsWith(p, StringComparison.OrdinalIgnoreCase) ||
                    normalised.Equals(p.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            },
            progress: progress, ct: ct), ct);
    }

    // ── Single-file extraction (preview / open) ───────────────────────────────

    /// <summary>
    /// Extracts a single entry to a temporary directory and returns the path
    /// to the extracted file (for preview or open-with operations).
    /// The caller is responsible for deleting the temp directory when done.
    /// </summary>
    public async Task<string?> ExtractToTempAsync(
        string archivePath,
        ArchiveEntry entry,
        string? password = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Winpress_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        var entryPath = entry.FullPath.Replace('\\', '/');

        await Task.Run(() => DoExtract(archivePath, tempDir, password: password,
            filter: key =>
            {
                var k = (key ?? string.Empty).Replace('\\', '/');
                return k.Equals(entryPath, StringComparison.OrdinalIgnoreCase);
            },
            progress: null, ct: ct), ct);

        // Return the extracted file path
        var name = Path.GetFileName(entry.FullPath);
        var extracted = Path.Combine(tempDir, name);
        return File.Exists(extracted) ? extracted : null;
    }

    // ── Core extraction logic ─────────────────────────────────────────────────

    private static void DoExtract(
        string archivePath,
        string destination,
        string? password,
        Func<string?, bool>? filter,
        IProgress<ExtractionProgress>? progress,
        CancellationToken ct)
    {
        var options = new ReaderOptions
        {
            Password        = password,
            LeaveStreamOpen = false,
            LookForHeader   = true
        };

        using var archive = ArchiveFactory.Open(archivePath, options);
        Directory.CreateDirectory(destination);

        var extractOptions = new ExtractionOptions
        {
            ExtractFullPath = true,
            Overwrite       = true,
            PreserveFileTime = true
        };

        // Pre-calculate total bytes for progress
        var entries = archive.Entries
            .Where(e => !e.IsDirectory && (filter == null || filter(e.Key)))
            .ToList();

        var totalBytes = entries.Sum(e => e.Size);
        var totalFiles = entries.Count;
        long doneBytes = 0;
        var  doneFiles = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(entry.Key?.Replace('\\', '/') ?? "file");
            progress?.Report(new ExtractionProgress(name, doneFiles, totalFiles, doneBytes, totalBytes));

            entry.WriteToDirectory(destination, extractOptions);

            doneBytes += entry.Size;
            doneFiles++;
        }

        // Final progress (100 %)
        progress?.Report(new ExtractionProgress(string.Empty, totalFiles, totalFiles, totalBytes, totalBytes));
    }
}
