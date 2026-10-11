using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Winpress.Services;

/// <summary>
/// Registers Winpress as a valid handler for archive file types so it can
/// appear in Windows "Default Apps" settings.
///
/// Modern Windows 10/11 restricts programmatic default app changes (to prevent
/// malware from hijacking associations). The correct flow is:
///   1. Register Winpress as a valid handler in the registry (HKCU).
///   2. Launch the Windows Default Apps settings page.
///   3. The user manually selects Winpress.
///
/// All writes go to HKEY_CURRENT_USER — no administrator rights required.
/// </summary>
public class FileAssociationService
{
    private const string AppName      = "Winpress";
    private const string AppId        = "Winpress.Application";
    private const string ProgIdPrefix = "Winpress";
    private const string HkcuRoot     = @"SOFTWARE\Classes";

    private static readonly string[] Extensions =
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
        ".tgz", ".tbz2", ".txz", ".lz4", ".zst",
        ".iso", ".wim", ".cab",
        ".deb", ".rpm", ".cpio", ".ar"
    };

    /// <summary>
    /// Registers Winpress as a valid handler for all supported archive formats
    /// and opens the Windows Default Apps settings page so the user can select it.
    /// </summary>
    public static void RegisterAndOpenSettings()
    {
        RegisterCapabilities();
        OpenDefaultAppsSettings();
    }

    /// <summary>
    /// Registers Winpress as a valid handler for all supported archive formats
    /// in the registry. Does NOT change the default app — the user must do that
    /// manually in Windows Settings.
    /// </summary>
    public static void RegisterCapabilities()
    {
        var exePath = Environment.ProcessPath ?? GetExePath();

        // 1. Register the main ProgId (Winpress.Application)
        RegisterProgId(exePath);

        // 2. Register each extension with its own ProgId
        foreach (var ext in Extensions)
            RegisterExtension(ext, exePath);

        // 3. Register application capabilities (makes Winpress appear in Default Apps)
        RegisterApplicationCapabilities();

        // 4. Register in RegisteredApplications
        RegisterApplication();
    }

    /// <summary>
    /// Removes Winpress's default-app registration from the registry.
    /// Does NOT remove context-menu entries (use ShellIntegrationService.Unregister for that).
    /// </summary>
    public static void UnregisterCapabilities()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{HkcuRoot}\{AppId}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"{HkcuRoot}\Applications\{AppName}.exe", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"SOFTWARE\{AppName}", throwOnMissingSubKey: false);

            foreach (var ext in Extensions)
            {
                var progId = $@"{HkcuRoot}\{ProgIdPrefix}{ext}";
                Registry.CurrentUser.DeleteSubKeyTree(progId, throwOnMissingSubKey: false);
            }

            // Remove from RegisteredApplications
            using var registeredApps = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\RegisteredApplications", writable: true);
            registeredApps?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch
        {
            // Best-effort removal
        }
    }

    /// <summary>
    /// Opens the Windows Default Apps settings page to the archive/compression category.
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "ms-settings:defaultapps",
                UseShellExecute = true
            });
        }
        catch
        {
            // Fallback: open the general default apps page
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = "control",
                    Arguments       = "/name Microsoft.DefaultPrograms",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Silently fail if we can't open settings
            }
        }
    }

    /// <summary>
    /// Checks if Winpress is registered as a handler (not necessarily the default).
    /// </summary>
    public static bool IsRegistered()
    {
        var key = $@"{HkcuRoot}\{AppId}\shell\open\command";
        using var reg = Registry.CurrentUser.OpenSubKey(key);
        return reg != null;
    }

    // ── Registration helpers ──────────────────────────────────────────────────

    private static void RegisterProgId(string exePath)
    {
        var progIdKey = $@"{HkcuRoot}\{AppId}";
        using (var key = Registry.CurrentUser.CreateSubKey(progIdKey))
        {
            key.SetValue("", $"{AppName} Archive");
            key.SetValue("FriendlyTypeName", $"{AppName} Archive Handler");
        }

        using (var key = Registry.CurrentUser.CreateSubKey($@"{progIdKey}\DefaultIcon"))
        {
            key.SetValue("", $"\"{exePath}\",0");
        }

        using (var key = Registry.CurrentUser.CreateSubKey($@"{progIdKey}\shell\open"))
        {
            key.SetValue("", "Open");
        }

        using (var key = Registry.CurrentUser.CreateSubKey($@"{progIdKey}\shell\open\command"))
        {
            key.SetValue("", $"\"{exePath}\" \"%1\"");
        }
    }

    private static void RegisterExtension(string extension, string exePath)
    {
        var progId = $@"{HkcuRoot}\{ProgIdPrefix}{extension}";

        using (var key = Registry.CurrentUser.CreateSubKey(progId))
        {
            key.SetValue("", $"{AppName} {extension} File");
        }

        using (var key = Registry.CurrentUser.CreateSubKey($@"{progId}\DefaultIcon"))
        {
            key.SetValue("", $"\"{exePath}\",0");
        }

        using (var key = Registry.CurrentUser.CreateSubKey($@"{progId}\shell\open"))
        {
            key.SetValue("", $"Open with {AppName}");
        }

        using (var key = Registry.CurrentUser.CreateSubKey($@"{progId}\shell\open\command"))
        {
            key.SetValue("", $"\"{exePath}\" \"%1\"");
        }

        // Register OpenWithProgids under the extension's SystemFileAssociations
        var openWithKey = $@"{HkcuRoot}\SystemFileAssociations\{extension}\OpenWithProgids";
        using (var key = Registry.CurrentUser.CreateSubKey(openWithKey))
        {
            key.SetValue($"{ProgIdPrefix}{extension}", new byte[0], RegistryValueKind.Binary);
        }
    }

    private static void RegisterApplicationCapabilities()
    {
        var capKey = $@"SOFTWARE\{AppName}\Capabilities";

        using (var key = Registry.CurrentUser.CreateSubKey(capKey))
        {
            key.SetValue("ApplicationName", AppName);
            key.SetValue("ApplicationDescription", "Archive manager for Windows");
            key.SetValue("ApplicationIcon", $"\"{Environment.ProcessPath ?? GetExePath()}\",0");
        }

        var fileAssocKey = $@"{capKey}\FileAssociations";
        using (var key = Registry.CurrentUser.CreateSubKey(fileAssocKey))
        {
            foreach (var ext in Extensions)
            {
                key.SetValue(ext, $"{ProgIdPrefix}{ext}");
            }
        }
    }

    private static void RegisterApplication()
    {
        using var registeredApps = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\RegisteredApplications");
        registeredApps?.SetValue(AppName, $@"SOFTWARE\{AppName}\Capabilities");
    }

    private static string GetExePath() =>
        Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Winpress.exe");
}
