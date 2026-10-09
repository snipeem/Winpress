using System.IO;
using Microsoft.Win32;

namespace Winpress.Services;

/// <summary>
/// Registers and unregisters Windows Explorer context-menu entries for the
/// archive formats Winpress supports.
///
/// All writes go to HKEY_CURRENT_USER — no administrator rights required.
///
/// Registered menu items:
///   • "Open with Winpress"   — opens the archive browser
///   • "Extract Here"          — extracts in the same folder
///   • "Extract to Folder…"    — shows a folder picker, then extracts
/// </summary>
public class ShellIntegrationService
{
    private const string AppName    = "Winpress";
    private const string HkcuRoot   = @"SOFTWARE\Classes";

    private static readonly string[] Extensions =
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
        ".tgz", ".tbz2", ".txz", ".lz4", ".zst",
        ".iso", ".wim", ".cab",
        ".deb", ".rpm", ".cpio", ".ar"
    };

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Registers context-menu verbs for all supported extensions.
    /// Uses the currently running executable's path automatically.
    /// </summary>
    public static void Register()
    {
        var exePath = Environment.ProcessPath ?? GetExePath();
        foreach (var ext in Extensions)
            RegisterForExtension(ext, exePath);
    }

    private static void RegisterForExtension(string extension, string exePath)
    {
        // We register under SystemFileAssociations so the menu appears regardless
        // of which program "owns" the extension.
        var baseKey = $@"{HkcuRoot}\SystemFileAssociations\{extension}\shell\{AppName}";

        // Parent key — shows as a submenu with the Winpress label
        using (var key = Registry.CurrentUser.CreateSubKey(baseKey))
        {
            key.SetValue("",         $"&Winpress");
            key.SetValue("Icon",     $"\"{exePath}\",0");
            key.SetValue("SubCommands", string.Empty);
        }

        // Sub-verb: Open with Winpress
        using (var key = Registry.CurrentUser.CreateSubKey($@"{baseKey}\shell\open"))
        {
            key.SetValue("", "Open with Winpress");
            using var cmd = key.CreateSubKey("command");
            cmd.SetValue("", $"\"{exePath}\" \"%1\"");
        }

        // Sub-verb: Extract Here
        using (var key = Registry.CurrentUser.CreateSubKey($@"{baseKey}\shell\extracthere"))
        {
            key.SetValue("", "Extract Here");
            using var cmd = key.CreateSubKey("command");
            cmd.SetValue("", $"\"{exePath}\" --extract-here \"%1\"");
        }

        // Sub-verb: Extract to Folder…
        using (var key = Registry.CurrentUser.CreateSubKey($@"{baseKey}\shell\extractto"))
        {
            key.SetValue("", "Extract to Folder\u2026");
            using var cmd = key.CreateSubKey("command");
            cmd.SetValue("", $"\"{exePath}\" --extract-to \"%1\"");
        }
    }

    // ── Removal ───────────────────────────────────────────────────────────────

    /// <summary>Removes all Winpress context-menu entries from the registry.</summary>
    public static void Unregister()
    {
        foreach (var ext in Extensions)
            UnregisterForExtension(ext);
    }

    private static void UnregisterForExtension(string extension)
    {
        var baseKey = $@"{HkcuRoot}\SystemFileAssociations\{extension}\shell\{AppName}";
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(baseKey, throwOnMissingSubKey: false);
        }
        catch { /* best-effort */ }
    }

    // ── Status check ─────────────────────────────────────────────────────────

    /// <summary>Returns true if Winpress context menus are currently registered.</summary>
    public static bool IsRegistered()
    {
        var key = $@"{HkcuRoot}\SystemFileAssociations\.zip\shell\{AppName}";
        using var reg = Registry.CurrentUser.OpenSubKey(key);
        return reg != null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetExePath() =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Winpress.exe");
}
