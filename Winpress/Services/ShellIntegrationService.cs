using System.IO;
using System.Runtime.InteropServices;
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
///   • "Compress to ZIP/7z/…"  — creates archives in various formats
/// </summary>
public class ShellIntegrationService
{
    private const string AppName    = "Winpress";
    private const string HkcuRoot   = @"SOFTWARE\Classes";

    // Shell change notification constants
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST       = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

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
        var exePath = GetExecutablePath();
        foreach (var ext in Extensions)
            RegisterForExtension(ext, exePath);

        // Register "Compress with Winpress" on ALL file types
        RegisterForAllFiles(exePath);

        // Register "Compress with Winpress" on FOLDER right-click
        RegisterForDirectory(exePath);

        // Notify Explorer to refresh its context menu cache
        RefreshExplorerCache();
    }

    private static void RegisterForAllFiles(string exePath)
    {
        var baseKey = $@"{HkcuRoot}\*\shell\{AppName}";
        using (var key = Registry.CurrentUser.CreateSubKey(baseKey))
        {
            key.SetValue("",            "&Winpress");
            key.SetValue("Icon",        $"\"{exePath}\",0");
            key.SetValue("SubCommands", string.Empty);
        }
        RegisterCompressVerbs(baseKey, exePath, 1);
    }

    private static void RegisterForDirectory(string exePath)
    {
        foreach (var branch in new[] { @"Directory\shell", @"Directory\Background\shell" })
        {
            var baseKey = $@"{HkcuRoot}\{branch}\{AppName}";
            using (var key = Registry.CurrentUser.CreateSubKey(baseKey))
            {
                key.SetValue("",            "&Winpress");
                key.SetValue("Icon",        $"\"{exePath}\",0");
                key.SetValue("SubCommands", string.Empty);
            }
            var placeholder = branch.Contains("Background") ? "%V" : "%1";
            RegisterCompressVerbsWithPlaceholder(baseKey, exePath, placeholder, 1);
        }
    }

    private static void RegisterCompressVerbsWithPlaceholder(
        string baseKey, string exePath, string ph, int startIndex)
    {
        void Verb(string id, string label, string arg)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{baseKey}\shell\{id}");
            key.SetValue("", label);
            using var cmd = key.CreateSubKey("command");
            cmd.SetValue("", $"\"{exePath}\" {arg} \"{ph}\"");
        }

        Verb($"{startIndex:D2}_zip",    "Compress to ZIP",           "--compress-here zip");
        Verb($"{startIndex+1:D2}_7z",   "Compress to 7z",            "--compress-here 7z");
        Verb($"{startIndex+2:D2}_tgz",  "Compress to TAR.GZ",        "--compress-here tar.gz");
        Verb($"{startIndex+3:D2}_tzst", "Compress to TAR+Zstandard", "--compress-here tar.zst");
        Verb($"{startIndex+4:D2}_add",  "Add to Archive\u2026",      "--add-to-archive");
    }

    private static void RegisterForExtension(string extension, string exePath)
    {
        var baseKey = $@"{HkcuRoot}\SystemFileAssociations\{extension}\shell\{AppName}";

        // Parent — submenu header
        using (var key = Registry.CurrentUser.CreateSubKey(baseKey))
        {
            key.SetValue("",            "&Winpress");
            key.SetValue("Icon",        $"\"{exePath}\",0");
            key.SetValue("SubCommands", string.Empty);
        }

        // 01 Open
        using (var key = Registry.CurrentUser.CreateSubKey($@"{baseKey}\shell\01_open"))
        {
            key.SetValue("", "Open with Winpress");
            using var cmd = key.CreateSubKey("command");
            cmd.SetValue("", $"\"{exePath}\" \"%1\"");
        }

        // 02 Extract Here
        using (var key = Registry.CurrentUser.CreateSubKey($@"{baseKey}\shell\02_extracthere"))
        {
            key.SetValue("", "Extract Here");
            using var cmd = key.CreateSubKey("command");
            cmd.SetValue("", $"\"{exePath}\" --extract-here \"%1\"");
        }

        // 03 Extract to Folder…
        using (var key = Registry.CurrentUser.CreateSubKey($@"{baseKey}\shell\03_extractto"))
        {
            key.SetValue("", "Extract to Folder\u2026");
            using var cmd = key.CreateSubKey("command");
            cmd.SetValue("", $"\"{exePath}\" --extract-to \"%1\"");
        }

        // ── visual separator before compress group ─────────────────────────
        using (var key = Registry.CurrentUser.CreateSubKey($@"{baseKey}\shell\04_sep"))
            key.SetValue("CommandFlags", 0x40, RegistryValueKind.DWord);

        // 05–09  compress verbs (shared helper)
        RegisterCompressVerbs(baseKey, exePath);
    }

    // ── Compress verbs (reused for *, Directory, and archive extensions) ──────

    private static void RegisterCompressVerbs(string baseKey, string exePath, int startIndex = 5)
    {
        void Verb(string id, string label, string arg)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{baseKey}\shell\{id}");
            key.SetValue("", label);
            using var cmd = key.CreateSubKey("command");
            cmd.SetValue("", $"\"{exePath}\" {arg} \"%1\"");
        }

        Verb($"{startIndex:D2}_zip",    "Compress to ZIP",           "--compress-here zip");
        Verb($"{startIndex+1:D2}_7z",   "Compress to 7z",            "--compress-here 7z");
        Verb($"{startIndex+2:D2}_tgz",  "Compress to TAR.GZ",        "--compress-here tar.gz");
        Verb($"{startIndex+3:D2}_tzst", "Compress to TAR+Zstandard", "--compress-here tar.zst");
        Verb($"{startIndex+4:D2}_add",  "Add to Archive\u2026",      "--add-to-archive");
    }

    // ── Removal ───────────────────────────────────────────────────────────────

    /// <summary>Removes all Winpress context-menu entries from the registry.</summary>
    public static void Unregister()
    {
        foreach (var ext in Extensions)
            UnregisterForExtension(ext);

        TryDelete($@"{HkcuRoot}\*\shell\{AppName}");
        TryDelete($@"{HkcuRoot}\Directory\shell\{AppName}");
        TryDelete($@"{HkcuRoot}\Directory\Background\shell\{AppName}");

        // Notify Explorer to refresh its context menu cache
        RefreshExplorerCache();
    }

    private static void TryDelete(string key)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); }
        catch { /* best-effort */ }
    }

    private static void UnregisterForExtension(string extension)
    {
        TryDelete($@"{HkcuRoot}\SystemFileAssociations\{extension}\shell\{AppName}");
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

    /// <summary>
    /// Gets the path to the Winpress executable.
    /// For single-file self-contained apps, Environment.ProcessPath returns the correct path.
    /// Falls back to AppContext.BaseDirectory + "Winpress.exe" if needed.
    /// </summary>
    private static string GetExecutablePath()
    {
        // Environment.ProcessPath works for both regular and single-file deployments
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return path;

        // Fallback for edge cases
        return Path.Combine(AppContext.BaseDirectory, "Winpress.exe");
    }

    /// <summary>
    /// Notifies Windows Explorer to refresh its context menu cache.
    /// This is required for newly registered context menu items to appear.
    /// </summary>
    private static void RefreshExplorerCache()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Best-effort — if this fails, user may need to restart Explorer or log out/in
        }
    }
}
