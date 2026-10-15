namespace Winpress.Models;

/// <summary>Archive formats Winpress can CREATE.</summary>
public enum CompressionFormat
{
    Zip, SevenZip,
    TarGz, TarBz2, TarXz, TarZst, Tar,
    GZip, BZip2, ZStandard
}

public static class CompressionFormatExtensions
{
    public static string ToExtension(this CompressionFormat f) => f switch
    {
        CompressionFormat.Zip       => ".zip",
        CompressionFormat.SevenZip  => ".7z",
        CompressionFormat.TarGz     => ".tar.gz",
        CompressionFormat.TarBz2    => ".tar.bz2",
        CompressionFormat.TarXz     => ".tar.xz",
        CompressionFormat.TarZst    => ".tar.zst",
        CompressionFormat.Tar       => ".tar",
        CompressionFormat.GZip      => ".gz",
        CompressionFormat.BZip2     => ".bz2",
        CompressionFormat.ZStandard => ".zst",
        _                           => ".zip"
    };

    public static string ToDisplayName(this CompressionFormat f) => f switch
    {
        CompressionFormat.Zip       => "ZIP (.zip)  — universal, optional AES-256",
        CompressionFormat.SevenZip  => "7-Zip (.7z)  — best ratio, AES-256",
        CompressionFormat.TarGz     => "TAR + GZip (.tar.gz)  — Linux standard",
        CompressionFormat.TarBz2    => "TAR + BZip2 (.tar.bz2)  — compact",
        CompressionFormat.TarXz     => "TAR + XZ (.tar.xz)  — best TAR ratio",
        CompressionFormat.TarZst    => "TAR + Zstandard (.tar.zst)  — fast + small",
        CompressionFormat.Tar       => "TAR (.tar)  — uncompressed container",
        CompressionFormat.GZip      => "GZip (.gz)  — single file",
        CompressionFormat.BZip2     => "BZip2 (.bz2)  — single file",
        CompressionFormat.ZStandard => "Zstandard (.zst)  — single file",
        _                           => "ZIP (.zip)"
    };

    public static string ToShortLabel(this CompressionFormat f) => f switch
    {
        CompressionFormat.Zip       => "zip",
        CompressionFormat.SevenZip  => "7z",
        CompressionFormat.TarGz     => "tar.gz",
        CompressionFormat.TarBz2    => "tar.bz2",
        CompressionFormat.TarXz     => "tar.xz",
        CompressionFormat.TarZst    => "tar.zst",
        CompressionFormat.Tar       => "tar",
        CompressionFormat.GZip      => "gz",
        CompressionFormat.BZip2     => "bz2",
        CompressionFormat.ZStandard => "zst",
        _                           => "zip"
    };

    public static CompressionFormat FromShortLabel(string label) =>
        label.ToLowerInvariant() switch
        {
            "zip"     => CompressionFormat.Zip,
            "7z"      => CompressionFormat.SevenZip,
            "tar.gz"  => CompressionFormat.TarGz,
            "tar.bz2" => CompressionFormat.TarBz2,
            "tar.xz"  => CompressionFormat.TarXz,
            "tar.zst" => CompressionFormat.TarZst,
            "tar"     => CompressionFormat.Tar,
            "gz"      => CompressionFormat.GZip,
            "bz2"     => CompressionFormat.BZip2,
            "zst"     => CompressionFormat.ZStandard,
            _         => CompressionFormat.Zip
        };

    /// <summary>True when the format supports password / encryption.</summary>
    public static bool SupportsPassword(this CompressionFormat f) =>
        f is CompressionFormat.Zip or CompressionFormat.SevenZip;

    /// <summary>True when the format can hold more than one file.</summary>
    public static bool SupportsMultipleFiles(this CompressionFormat f) =>
        f is not (CompressionFormat.GZip or CompressionFormat.BZip2 or CompressionFormat.ZStandard);
}
