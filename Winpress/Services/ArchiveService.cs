using System.IO;
using System.Security.Cryptography;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using Winpress.Models;

namespace Winpress.Services;

/// <summary>
/// Result returned by <see cref="ArchiveService.OpenAsync"/>.
/// </summary>
public record ArchiveOpenResult(
    List<ArchiveEntry>? Entries,
    bool NeedsPassword,
    string? ErrorMessage
);

/// <summary>
/// Opens archive files and returns a flat list of all entries.
/// Supports all formats handled by SharpCompress (ZIP, 7z, TAR, RAR,
/// GZip, BZip2, XZ, CPIO, AR/DEB, LZ4, and more).
///
/// Directory navigation (drilling into sub-folders) is handled by
/// <see cref="MainViewModel"/> which filters this flat list in memory —
/// no re-reading required on each navigation.
/// </summary>
public class ArchiveService
{
    /// <summary>
    /// Opens the archive and returns a flat list of all entries.
    /// </summary>
    /// <param name="filePath">Path to the archive file.</param>
    /// <param name="password">Optional password for encrypted archives.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ArchiveOpenResult> OpenAsync(
        string filePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var options = new ReaderOptions
                {
                    Password         = password,
                    LeaveStreamOpen  = false,
                    LookForHeader    = true    // detect format even with wrong extension
                };

                using var archive = ArchiveFactory.Open(filePath, options);

                // If the first encrypted entry is encountered without a password, bail.
                var entries = new List<ArchiveEntry>();

                foreach (var e in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (e.IsEncrypted && password == null)
                        return new ArchiveOpenResult(null, true, null);

                    var rawKey = e.Key ?? string.Empty;
                    // Normalise separators and trailing slashes
                    var fullPath = rawKey.Replace('\\', '/').TrimEnd('/');
                    var name     = Path.GetFileName(fullPath);
                    if (string.IsNullOrEmpty(name)) name = fullPath; // root-level entry

                    entries.Add(new ArchiveEntry
                    {
                        Name           = name,
                        FullPath       = fullPath,
                        IsDirectory    = e.IsDirectory,
                        Size           = e.Size,
                        CompressedSize = e.CompressedSize,
                        LastModified   = e.LastModifiedTime,
                        IsEncrypted    = e.IsEncrypted
                    });
                }

                // Build virtual directory entries for paths that contain no
                // explicit directory entry (common in many archivers).
                var withVirtual = AddVirtualDirectories(entries);

                return new ArchiveOpenResult(withVirtual, false, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (CryptographicException)
            {
                return new ArchiveOpenResult(null, true, null);
            }
            catch (InvalidFormatException ex)
            {
                // SharpCompress throws this for wrong-password RAR/7z
                if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
                    return new ArchiveOpenResult(null, true, null);
                return new ArchiveOpenResult(null, false, $"Unsupported or corrupt archive: {ex.Message}");
            }
            catch (Exception ex)
            {
                return new ArchiveOpenResult(null, false, ex.Message);
            }
        }, cancellationToken);
    }

    // ── Virtual directory synthesis ───────────────────────────────────────────
    /// <summary>
    /// Inspects all entry paths and adds synthesised <see cref="ArchiveEntry"/>
    /// records for any intermediate directory that has no real entry of its own.
    ///
    /// Example: an archive may contain "docs/readme.txt" without a "docs/" entry.
    /// Without this step the folder would be invisible in the tree view.
    /// </summary>
    private static List<ArchiveEntry> AddVirtualDirectories(List<ArchiveEntry> entries)
    {
        var existingPaths = new HashSet<string>(
            entries.Where(e => e.IsDirectory).Select(e => e.FullPath),
            StringComparer.OrdinalIgnoreCase);

        var toAdd = new Dictionary<string, ArchiveEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var parts = entry.FullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var depth = 1; depth < parts.Length; depth++)
            {
                var dirPath = string.Join("/", parts, 0, depth);
                if (!existingPaths.Contains(dirPath) && !toAdd.ContainsKey(dirPath))
                {
                    toAdd[dirPath] = new ArchiveEntry
                    {
                        Name        = parts[depth - 1],
                        FullPath    = dirPath,
                        IsDirectory = true,
                        IsVirtual   = true
                    };
                }
            }
        }

        var result = new List<ArchiveEntry>(entries.Count + toAdd.Count);
        result.AddRange(entries);
        result.AddRange(toAdd.Values);
        return result;
    }
}
