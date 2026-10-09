using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Winpress.Models;
using Winpress.ViewModels;
using Winpress.Views;

namespace Winpress;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    // ── Construction ─────────────────────────────────────────────────────────

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        // Wire up events that require UI-layer dialogs
        _vm.PasswordRequested     = ShowPasswordDialogAsync;
        _vm.FolderPickerRequested = ShowFolderPickerAsync;
        _vm.ShowProgressWindow    = ShowProgressWindow;
    }

    // ── Public helpers (called from App.xaml.cs for shell args) ──────────────

    public void OpenArchive(string path) =>
        _vm.OpenArchiveCommand.Execute(path);

    public void ExtractHere(string path) =>
        _vm.ExtractHereCommand.Execute(path);

    public void ExtractToFolder(string archive, string? dest)
    {
        if (dest != null)
            _vm.ExtractToPathCommand.Execute((archive, dest));
        else
            _vm.ExtractHereCommand.Execute(archive);
    }

    // ── Menu / toolbar click handlers ────────────────────────────────────────

    private void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        var path = ShowOpenFileDialog();
        if (path != null) _vm.OpenArchiveCommand.Execute(path);
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)  => Close();

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Winpress  v" + Winpress.Services.UpdateService.CurrentVersion + "\n\n" +
            "The archive manager Windows should have shipped with.\n\n" +
            "ZIP · 7z · RAR · TAR · GZ · BZ2 · XZ · ISO · WIM · CAB and more\n\n" +
            "Open source · GPL-3.0 · github.com/YOUR_GITHUB_USERNAME/Winpress",
            "About Winpress",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void MenuGitHub_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/YOUR_GITHUB_USERNAME/Winpress");

    // ── Drag & drop ───────────────────────────────────────────────────────────

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            _vm.OpenArchiveCommand.Execute(files[0]);
    }

    // ── ListView interactions ─────────────────────────────────────────────────

    private void ArchiveListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm.SelectedEntries = ArchiveListView.SelectedItems
            .Cast<ArchiveEntry>()
            .ToList();
    }

    private async void ArchiveListView_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ArchiveListView.SelectedItem is ArchiveEntry entry)
            await _vm.NavigateIntoEntryCommand.ExecuteAsync(entry);
    }

    private async void ArchiveListView_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (ArchiveListView.SelectedItem is ArchiveEntry entry)
                    await _vm.NavigateIntoEntryCommand.ExecuteAsync(entry);
                break;

            case Key.Back:
                _vm.NavigateBackCommand.Execute(null);
                break;

            case Key.A when Keyboard.Modifiers == ModifierKeys.Control:
                ArchiveListView.SelectAll();
                break;
        }
    }

    // ── Column header sort ────────────────────────────────────────────────────

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader header && header.Column != null)
        {
            var column = (header.Content as string) ?? header.Column.Header?.ToString() ?? string.Empty;
            _vm.SortByCommand.Execute(column);
        }
    }

    // ── Global keyboard shortcuts ─────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.O when Keyboard.Modifiers == ModifierKeys.Control:
                MenuOpen_Click(this, new RoutedEventArgs());
                break;
            case Key.E when Keyboard.Modifiers == ModifierKeys.Control
                         && Keyboard.Modifiers != ModifierKeys.Shift:
                _vm.ExtractAllCommand.Execute(null);
                break;
            case Key.E when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                _vm.ExtractSelectedCommand.Execute(null);
                break;
            case Key.Left when Keyboard.Modifiers == ModifierKeys.Alt:
                _vm.NavigateBackCommand.Execute(null);
                break;
        }
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private async void ContextOpen_Click(object sender, RoutedEventArgs e)
    {
        if (ArchiveListView.SelectedItem is ArchiveEntry entry)
            await _vm.NavigateIntoEntryCommand.ExecuteAsync(entry);
    }

    private void ContextSelectAll_Click(object sender, RoutedEventArgs e) =>
        ArchiveListView.SelectAll();

    // ── UI service implementations (injected into ViewModel) ──────────────────

    private Task<string?> ShowPasswordDialogAsync(string archivePath)
    {
        var dialog = new PasswordDialog { Owner = this };
        return Task.FromResult(
            dialog.ShowDialog() == true ? dialog.Password : null);
    }

    private Task<string?> ShowFolderPickerAsync(string prompt)
    {
        // .NET 10 ships System.Windows.Forms; use the WinForms FolderBrowserDialog
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description         = prompt,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        var result = dlg.ShowDialog();
        return Task.FromResult(
            result == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null);
    }

    private void ShowProgressWindow(ExtractionProgressViewModel progressVm)
    {
        var win = new ProgressWindow(progressVm) { Owner = this };
        win.Show();
    }

    // ── File open dialog ──────────────────────────────────────────────────────

    private static string? ShowOpenFileDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Open Archive",
            Filter =
                "All Archives|*.zip;*.7z;*.rar;*.tar;*.gz;*.bz2;*.xz;*.tgz;*.tbz2;*.txz;" +
                "*.lz4;*.zst;*.cab;*.iso;*.wim;*.deb;*.rpm;*.cpio;*.ar;*.arj|" +
                "ZIP Archives|*.zip;*.zipx|" +
                "7-Zip Archives|*.7z|" +
                "RAR Archives|*.rar|" +
                "TAR Archives|*.tar;*.tgz;*.tbz2;*.txz|" +
                "Compressed files|*.gz;*.bz2;*.xz;*.lz4;*.zst|" +
                "Disk Images|*.iso;*.wim;*.img|" +
                "Package files|*.deb;*.rpm;*.cab;*.msi|" +
                "All Files|*.*"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = url,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
