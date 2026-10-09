using System.IO;
using System.Windows;
using Winpress.Services;
using Winpress.ViewModels;
using Winpress.Views;
// Explicit aliases — prevents ambiguity with System.Windows.Forms when UseWindowsForms=true
using Application      = System.Windows.Application;
using StartupEventArgs = System.Windows.StartupEventArgs;
using ExitEventArgs    = System.Windows.ExitEventArgs;

namespace Winpress;

public partial class App : Application
{
    private readonly UpdateService _updateService = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var vm     = new MainViewModel(new ArchiveService(), new ExtractionService());
        var window = new MainWindow(vm);
        window.Show();

        // Handle command-line arguments (shell context menus, file associations).
        // Patterns:
        //   Winpress.exe <archive>                     → open browser
        //   Winpress.exe --extract-here <archive>      → extract in-place
        //   Winpress.exe --extract-to <archive> <dest> → extract to folder
        //   Winpress.exe --replace-old <oldExePath>    → internal: self-update
        if (e.Args.Length > 0)
            HandleArgs(vm, e.Args);

        // Fire-and-forget background update check.
        // Shows a non-intrusive dialog when a newer release is on GitHub.
        _ = CheckForUpdatesAsync(window);
    }

    // ── Command-line dispatch ─────────────────────────────────────────────────

    private static void HandleArgs(MainViewModel vm, string[] args)
    {
        if (args[0].StartsWith("--"))
        {
            var cmd     = args[0];
            var archive = args.Length > 1 ? args[1] : null;
            var dest    = args.Length > 2 ? args[2] : null;

            if (archive == null) return;

            switch (cmd)
            {
                case "--extract-here":
                    if (File.Exists(archive))
                        vm.ExtractHereCommand.Execute(archive);
                    break;

                case "--extract-to":
                    if (File.Exists(archive) && dest != null)
                        vm.ExtractToPathCommand.Execute((archive, dest!));
                    break;

                default:
                    if (File.Exists(archive))
                        vm.OpenArchiveCommand.Execute(archive);
                    break;
            }
        }
        else if (File.Exists(args[0]))
        {
            vm.OpenArchiveCommand.Execute(args[0]);
        }
    }

    // ── OTA update check ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs in the background after the main window is visible.
    /// If GitHub has a newer release, shows the update dialog.
    /// Any exception is swallowed — the update check is best-effort.
    /// </summary>
    private async Task CheckForUpdatesAsync(Window owner)
    {
        try
        {
            // Give the UI a moment to fully render before making a network call.
            await Task.Delay(TimeSpan.FromSeconds(3));

            using var cts    = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var       info   = await _updateService.CheckForUpdateAsync(cts.Token);

            if (info is null) return; // already up-to-date or no network

            // Switch back to the UI thread to show the dialog.
            await Dispatcher.InvokeAsync(() =>
            {
                var vm     = new UpdateViewModel(info, _updateService);
                var dialog = new UpdateDialog(vm) { Owner = owner };
                dialog.Show(); // non-modal so the user can keep working
            });
        }
        catch
        {
            // Network issues, timeout, JSON parse errors — all silent.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateService.Dispose();
        base.OnExit(e);
    }
}
