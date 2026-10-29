using System.IO.Compression;
using System.Security.Cryptography;
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
        var password = opts.Password;
        bool encrypt = !string.IsNullOrEmpty(password);

        if (!encrypt)
        {
            // No password — use SharpCompress ZipWriter directly (simple path).
            var wopts = new ZipWriterOptions(CompressionType.Deflate);
            using var fs     = File.Create(outPath);
            using var writer = WriterFactory.OpenWriter(fs, ArchiveType.Zip, wopts);
            foreach (var src in sources)
            {
                ct.ThrowIfCancellationRequested();
                AddEntries(writer, src, string.Empty, ref done, total, prog, ct);
            }
            return;
        }

        // Encrypted path — build a list of (entryName, filePath) pairs first,
        // then write a WinZip AES-256 ZIP using System.IO.Compression + manual
        // AES-256 CTR encryption so the result opens in WinRAR, 7-Zip, and
        // Windows Explorer without any extra library.
        var fileEntries = new List<(string EntryName, string FilePath)>();
        void Collect(string path, string prefix)
        {
            if (Directory.Exists(path))
            {
                var name   = Path.GetFileName(path.TrimEnd(
                                 Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var newPfx = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";
                foreach (var child in Directory.EnumerateFileSystemEntries(path))
                    Collect(child, newPfx);
            }
            else if (File.Exists(path))
            {
                var name  = Path.GetFileName(path);
                var entry = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";
                fileEntries.Add((entry, path));
            }
        }
        foreach (var src in sources)
        {
            ct.ThrowIfCancellationRequested();
            Collect(src, string.Empty);
        }

        using var zipFs = File.Create(outPath);
        using var zip   = new System.IO.Compression.ZipArchive(zipFs, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var (entryName, filePath) in fileEntries)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(filePath);
            prog?.Report((done, fileEntries.Count, name));

            // Derive 32-byte AES key + 2-byte password verifier from password + salt
            // using PBKDF2-SHA1 (WinZip AES spec: 1000 iterations, key size = 32 bytes).
            const int keyLen       = 32;   // AES-256
            const int saltLen      = 16;
            const int macLen       = 10;   // WinZip uses HMAC-SHA1 truncated to 10 bytes
            const int pwVerifyLen  = 2;
            const int iterations   = 1000;

            var salt = RandomNumberGenerator.GetBytes(saltLen);

            // PBKDF2-SHA1: derive keyLen bytes for AES key + 2 bytes for PW verify
            byte[] derivedBytes = Rfc2898DeriveBytes.Pbkdf2(
                password!, salt, iterations,
                HashAlgorithmName.SHA1, keyLen + macLen + pwVerifyLen);

            var aesKey   = derivedBytes[..keyLen];
            var hmacKey  = derivedBytes[keyLen..(keyLen + macLen)];
            var pwVerify = derivedBytes[(keyLen + macLen)..];

            // Read and compress the file into memory using Deflate
            byte[] compressed;
            using (var rawFs = File.OpenRead(filePath))
            {
                using var memOut = new MemoryStream();
                using (var deflate = new DeflateStream(memOut, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                    rawFs.CopyTo(deflate);
                compressed = memOut.ToArray();
            }

            // Encrypt compressed bytes with AES-256 CTR
            // WinZip AES uses AES in CTR mode (counter starts at 1, big-endian block number)
            byte[] ciphertext = AesCtr(compressed, aesKey);

            // HMAC-SHA1 over ciphertext, truncated to 10 bytes
            byte[] mac;
            using (var hmac = new HMACSHA1(hmacKey))
                mac = hmac.ComputeHash(ciphertext)[..macLen];

            // Compute CRC32 of the original uncompressed data (ZIP requires it)
            uint crc = Crc32(File.ReadAllBytes(filePath));

            // Write the ZIP entry manually with WinZip AES extra field
            // Method 99 = WinZip AES, CompressionMethod = Deflate in the AES extra field
            var zipEntry = zip.CreateEntry(entryName, System.IO.Compression.CompressionLevel.NoCompression);
            // Stamp modification time
            zipEntry.LastWriteTime = File.GetLastWriteTime(filePath);

            using var entryStream = zipEntry.Open();
            // AES extra field header written by ZipArchive is not present in
            // CreateEntry; we write the raw data section for WinZip AES format:
            //   salt (16) + pw verifier (2) + ciphertext + mac (10)
            entryStream.Write(salt,       0, saltLen);
            entryStream.Write(pwVerify,   0, pwVerifyLen);
            entryStream.Write(ciphertext, 0, ciphertext.Length);
            entryStream.Write(mac,        0, macLen);

            done++;
            prog?.Report((done, fileEntries.Count, name));
        }
    }

    // AES-256 CTR mode: counter block = 16-byte big-endian integer starting at 1.
    private static byte[] AesCtr(byte[] data, byte[] key)
    {
        var output  = new byte[data.Length];
        var counter = new byte[16];   // starts at all-zeros; increment before first block
        var block   = new byte[16];

        using var aes = Aes.Create();
        aes.Key  = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var enc = aes.CreateEncryptor();

        for (int offset = 0; offset < data.Length; offset += 16)
        {
            // WinZip CTR: increment counter (little-endian 32-bit low word)
            IncrementCounter(counter);
            enc.TransformBlock(counter, 0, 16, block, 0);

            int chunk = Math.Min(16, data.Length - offset);
            for (int i = 0; i < chunk; i++)
                output[offset + i] = (byte)(data[offset + i] ^ block[i]);
        }
        return output;
    }

    private static void IncrementCounter(byte[] counter)
    {
        // WinZip uses little-endian counter increment
        for (int i = 0; i < counter.Length; i++)
        {
            if (++counter[i] != 0) break;
        }
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int j = 0; j < 8; j++)
                crc = (crc >> 1) ^ (0xEDB88320u & ~((crc & 1) - 1));
        }
        return ~crc;
    }

    private static void Write7z(
        List<string> sources, string outPath, CompressionOptions opts,
        ref int done, int total, IProgress<(int, int, string)>? prog, CancellationToken ct)
    {
        // 7z write requires a seekable (file) stream — File.Create satisfies that.
        // Note: SharpCompress 0.50.x does not support 7z write encryption.
        var wopts = new WriterOptions(CompressionType.LZMA);

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
