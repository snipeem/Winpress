using System.Reflection;
using System.Text.Json;

namespace Winpress.Services;

/// <summary>
/// Metadata for an available update returned by <see cref="UpdateService.CheckForUpdateAsync"/>.
/// </summary>
public record UpdateInfo(
    string         Version,
    string         DownloadUrl,
    string         Changelog,
    DateTimeOffset PublishedAt
);

/// <summary>
/// Handles the full over-the-air update lifecycle:
///   1. Check GitHub Releases API for a newer version.
///   2. Download the new <c>Winpress.exe</c> to a temp file.
///   3. Apply: write a tiny CMD script that waits for this process
///      to exit, replaces the .exe, then re-launches the app.
///
/// Configuration: set <see cref="RepoOwner"/> and <see cref="RepoName"/>
/// after you fork/rename the GitHub repository.
/// </summary>
public class UpdateService : IDisposable
{
    // ── Repository coordinates ─────────────────────────────────────────────
    //  Change these to match YOUR GitHub account and repository name.
    public const string RepoOwner = "snipeem";
    public const string RepoName  = "Winpress";

    private static readonly string ApiUrl =
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    // ── HTTP client ────────────────────────────────────────────────────────
    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient();
        // GitHub API requires a User-Agent header.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Winpress/{CurrentVersion}");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Checks GitHub for a release newer than the running assembly version.
    /// Returns <c>null</c> when up-to-date, on network failure, or when no
    /// suitable asset is found — all silently, so startup is never blocked.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(ApiUrl, ct);
            using var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName   = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var body      = root.GetProperty("body").GetString()     ?? string.Empty;
            var published = root.GetProperty("published_at").GetDateTimeOffset();

            // We look for an asset named exactly "Winpress.exe"
            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? string.Empty;
                    if (name.Equals("Winpress.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            if (downloadUrl is null) return null;

            var latest  = ParseSemVer(tagName);
            var current = ParseSemVer(CurrentVersion);

            return latest > current
                ? new UpdateInfo(tagName, downloadUrl, body, published)
                : null;
        }
        catch
        {
            // Network error, malformed JSON, missing fields — all silent.
            return null;
        }
    }

    /// <summary>
    /// Downloads the update asset to a temp file, reporting 0-1 progress.
    /// Returns the local path of the downloaded file.
    /// </summary>
    public async Task<string> DownloadUpdateAsync(
        string                  downloadUrl,
        IProgress<double>?      progress = null,
        CancellationToken       ct       = default)
    {
        var destPath = Path.Combine(Path.GetTempPath(), "Winpress_pending_update.exe");

        using var response = await _http.GetAsync(
            downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file   = new FileStream(destPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 81_920, useAsync: true);

        var buffer     = new byte[81_920];
        long received  = 0;
        int  bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            received += bytesRead;
            if (totalBytes > 0)
                progress?.Report((double)received / totalBytes);
        }

        return destPath;
    }

    /// <summary>
    /// Applies an already-downloaded update by:
    ///   1. Writing a self-deleting CMD script to %TEMP%.
    ///   2. Launching it detached (hidden window).
    ///   3. Shutting down the current process.
    ///
    /// The script waits 2 s for the current .exe to release its file lock,
    /// moves the new .exe over the old one, then re-launches the app.
    ///
    /// Note: requires write access to the folder containing Winpress.exe.
    /// Portable installs (Desktop, Downloads, etc.) always satisfy this;
    /// Program Files installs would need elevation — we document this in
    /// the update dialog.
    /// </summary>
    public void ApplyUpdate(string downloadedExePath)
    {
        var currentExe = Environment.ProcessPath
            ?? Assembly.GetExecutingAssembly().Location;

        var scriptPath = Path.Combine(Path.GetTempPath(), "winpress_selfupdate.cmd");

        // The script intentionally uses move (atomic on the same volume) and
        // falls back gracefully if the destination is locked or on another drive.
        var script = $"""
            @echo off
            timeout /t 2 /nobreak > nul
            move /y "{downloadedExePath}" "{currentExe}"
            if errorlevel 1 (
                echo Update could not be applied automatically.
                echo Please close Winpress and manually replace:
                echo   {downloadedExePath}
                echo with:
                echo   {currentExe}
                pause
                exit /b 1
            )
            start "" "{currentExe}"
            del "%~f0"
            """;

        File.WriteAllText(scriptPath, script);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = scriptPath,
            UseShellExecute = true,
            WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden,
            CreateNoWindow  = true
        });

        // Shut down — the script will relaunch the updated binary.
        System.Windows.Application.Current.Dispatcher.Invoke(
            () => System.Windows.Application.Current.Shutdown());
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Running version string, e.g. "1.2.3".</summary>
    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    private static Version ParseSemVer(string tag)
    {
        var clean = tag.TrimStart('v').Split('-')[0]; // strip pre-release suffix
        return Version.TryParse(clean, out var v) ? v : new Version(0, 0, 0);
    }

    public void Dispose() => _http.Dispose();
}
