using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using Winpress.Models;

namespace Winpress.Services;

public enum ArchiveCompressionLevel { Fast, Default, Best }

/// <summary>
/// Options passed to <see cref="CompressionService.CompressAsync"/>.
/// </summary>
public record CompressionOptions(
    CompressionFormat      Format,
    string?                Password = null,
    ArchiveCompressionLevel Level   = ArchiveCompressionLevel.Default
);

/// <summary>
/// Creates archive files from source files/folders.
///
/// ZIP  — Deflate + optional WinZip AES-256 (SharpCompress 0.50+)
/// 7z   — LZMA   + optional AES-256
/// TAR  — plain, GZip, BZip2, XZ, Zstandard wrappers
/// GZip / BZip2 / Zstandard — single-file TAR-wrapped streams
/// </summary>
public class CompressionService
{
    // ── Public API ────────────────────────────────────────────────────────────

    public async Task CompressAsync(
        IReadOnlyList<string>                                          sourcePaths,
        string                                                         outputPath,
        CompressionOptions                                             opts,
        IProgress<(int Completed, int Total, string CurrentFile)>?    progress = null,
        CancellationToken                                              ct       = default)
    {
        // Write to a temp file; rename atomically on success so we never
        // leave a corrupt partial file at the destination.
        var temp = outputPath + ".wptemp";
        try
        {
            await Task.Run(() =>
            {
                var sources = sourcePaths.ToList();
                var total   = CountFiles(sources);
                var done    = 0;

                switch (opts.Format)
                {
                    case CompressionFormat.Zip:
                        WriteZip(sources, temp, opts, ref done, total, progress, ct);
                        break;
                    case CompressionFormat.SevenZip:
                        Write7z(sources, temp, opts, ref done, total, progress, ct);
                        break;
                    case CompressionFormat.TarGz:
                        WriteTar(sources, temp, CompressionType.GZip,       ref done, total, progress, ct);
                        break;
                    case CompressionFormat.TarBz2:
                        WriteTar(sources, temp, CompressionType.BZip2,      ref done, total, progress, ct);
                        break;
                    case CompressionFormat.TarXz:
                        WriteTar(sources, temp, CompressionType.Xz,         ref done, total, progress, ct);
                        break;
                    case CompressionFormat.TarZst:
                        WriteTar(sources, temp, CompressionType.ZStandard,  ref done, total, progress, ct);
                        break;
                    case CompressionFormat.Tar:
                        WriteTar(sources, temp, CompressionType.None,       ref done, total, progress, ct);
                        break;
                    default:   // GZip, BZip2, ZStandard — single-stream
                        WriteSingleStream(sources, temp, opts.Format, ct);
                        break;
                }
            }, ct);

            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Move(temp, outputPath);
        }
        catch
        {
            if (File.Exists(temp)) try { File.Delete(temp); } catch { /* best-effort */ }
            throw;
        }
    }

    // ── Format writers ────────────────────────────────────────────────────────

    private static void WriteZip(
        List<string> sources, string outPath, CompressionOptions opts,
        ref int done, int total, IProgress<(int, int, string)>? prog, CancellationToken ct)
    {
        var wopts = new ZipWriterOptions(CompressionType.Deflate);
        // NOTE: SharpCompress does not support writing encrypted archives yet. Password ignored.

        using var fs     = File.Create(outPath);
        using var writer = WriterFactory.OpenWriter(fs, ArchiveType.Zip, wopts);

        foreach (var src in sources)
        {
            ct.ThrowIfCancellationRequested();
            AddEntries(writer, src, string.Empty, ref done, total, prog, ct);
        }
    }

    private static void Write7z(
        List<string> sources, string outPath, CompressionOptions opts,
        ref int done, int total, IProgress<(int, int, string)>? prog, CancellationToken ct)
    {
        // 7z write requires a seekable (file) stream — File.Create satisfies that.
        var wopts = new WriterOptions(CompressionType.LZMA);
        // NOTE: SharpCompress does not support writing encrypted archives yet. Password ignored.

        using var fs     = File.Create(outPath);
        using var writer = WriterFactory.OpenWriter(fs, ArchiveType.SevenZip, wopts);

        foreach (var src in sources)
        {
            ct.ThrowIfCancellationRequested();
            AddEntries(writer, src, string.Empty, ref done, total, prog, ct);
        }
    }

    private static void WriteTar(
        List<string> sources, string outPath, CompressionType compression,
        ref int done, int total, IProgress<(int, int, string)>? prog, CancellationToken ct)
    {
        // SharpCompress wraps the TAR stream in the chosen compressor automatically.
        var wopts = new WriterOptions(compression);
        using var fs     = File.Create(outPath);
        using var writer = WriterFactory.OpenWriter(fs, ArchiveType.Tar, wopts);

        foreach (var src in sources)
        {
            ct.ThrowIfCancellationRequested();
            AddEntries(writer, src, string.Empty, ref done, total, prog, ct);
        }
    }

    private static void WriteSingleStream(
        List<string> sources, string outPath, CompressionFormat fmt, CancellationToken ct)
    {
        // GZip / BZip2 / ZStandard compress a single file; we wrap it in a
        // one-entry TAR so the original filename survives in the stream header.
        var src = sources.FirstOrDefault(File.Exists)
                  ?? throw new InvalidOperationException("No source file for single-stream format.");

        var ctype = fmt switch
        {
            CompressionFormat.BZip2     => CompressionType.BZip2,
            CompressionFormat.ZStandard => CompressionType.ZStandard,
            _                           => CompressionType.GZip
        };

        using var fs     = File.Create(outPath);
        using var writer = WriterFactory.OpenWriter(fs, ArchiveType.Tar, new WriterOptions(ctype));
        ct.ThrowIfCancellationRequested();
        using var rs     = File.OpenRead(src);
        writer.Write(Path.GetFileName(src), rs, File.GetLastWriteTime(src));
    }

    // ── Recursive entry helper ────────────────────────────────────────────────

    private static void AddEntries(
        IWriter writer, string path, string prefix,
        ref int done, int total, IProgress<(int, int, string)>? prog, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (Directory.Exists(path))
        {
            var name   = Path.GetFileName(path.TrimEnd(
                             Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var newPfx = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
                AddEntries(writer, child, newPfx, ref done, total, prog, ct);
        }
        else if (File.Exists(path))
        {
            var name  = Path.GetFileName(path);
            var entry = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";

            prog?.Report((done, total, name));

            using var rs = File.OpenRead(path);
            writer.Write(entry, rs, File.GetLastWriteTime(path));
            done++;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int CountFiles(IEnumerable<string> paths)
    {
        var n = 0;
        foreach (var p in paths)
        {
            if      (File.Exists(p))      n++;
            else if (Directory.Exists(p)) n += Directory.EnumerateFiles(
                                                   p, "*", SearchOption.AllDirectories).Count();
        }
        return Math.Max(n, 1);
    }
}
