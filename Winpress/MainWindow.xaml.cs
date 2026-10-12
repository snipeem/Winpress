using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Winpress.Models;
using Winpress.ViewModels;
using Winpress.Views;

// Explicit aliases — prevents ambiguity with System.Windows.Forms when UseWindowsForms=true
using DragEventArgs   = System.Windows.DragEventArgs;
using KeyEventArgs    = System.Windows.Input.KeyEventArgs;
using MessageBox      = System.Windows.MessageBox;
using DataFormats     = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using OpenFileDialog  = Microsoft.Win32.OpenFileDialog;
using MouseButtons    = System.Windows.Input.MouseButtonState;
using Point           = System.Windows.Point;
using MouseEventArgs  = System.Windows.Input.MouseEventArgs;
using DataObject      = System.Windows.DataObject;

namespace Winpress;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Winpress.Services.ExtractionService _extractionService;

    // ── DWM API for dark title bar ─────────────────────────────────────────
    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // ── Construction ─────────────────────────────────────────────────────────

    public MainWindow(MainViewModel vm, Winpress.Services.ExtractionService extractionService)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _extractionService = extractionService;

        // Wire up events that require UI-layer dialogs
        _vm.PasswordRequested     = ShowPasswordDialogAsync;
        _vm.FolderPickerRequested = ShowFolderPickerAsync;
        _vm.ShowProgressWindow    = ShowProgressWindow;
        _vm.ExtractFlatRequested  = ExtractFlatAsync;

        // Wire up drag-and-drop from the archive list.
        // PreviewMouseLeftButtonDown is used instead of MouseDown to intercept clicks
        // BEFORE WPF's internal selection logic runs. This is critical for multi-file
        // drag-and-drop: it allows us to prevent WPF from deselecting other items when
        // clicking on an already-selected item.
        ArchiveListView.PreviewMouseLeftButtonDown += ArchiveListView_PreviewMouseLeftButtonDown;
        ArchiveListView.MouseMove += ArchiveListView_MouseMove;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        
        // Apply Windows 11 dark mode to the native title bar
        var handle = new WindowInteropHelper(this).Handle;
        int trueValue = 1;
        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, sizeof(int));
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
            "Open source · GPL-3.0 · github.com/snipeem/Winpress",
            "About Winpress",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void MenuGitHub_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/snipeem/Winpress");

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

    // ── Drag-and-drop from archive list to external destinations ───────────────

    private Point _dragStartPoint;
    private bool _isDragging = false;
    private ArchiveEntry? _dragClickedEntry;

    private void ArchiveListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        
        // Hit test to find the clicked ListViewItem in the visual tree
        var clickedElement = e.OriginalSource as DependencyObject;
        var listViewItem = FindAncestor<ListViewItem>(clickedElement);
        _dragClickedEntry = listViewItem?.DataContext as ArchiveEntry;
        
        // If the clicked item is already part of the selection, prevent WPF from
        // changing the selection. This is the key fix for multi-file drag-and-drop:
        // without this, clicking on an already-selected item would deselect other
        // selected items, breaking the ability to drag multiple files at once.
        if (_dragClickedEntry != null && ArchiveListView.SelectedItems.Contains(_dragClickedEntry))
        {
            e.Handled = true;
        }
    }

    private async void ArchiveListView_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragClickedEntry == null)
            return;

        var currentPoint = e.GetPosition(null);
        var diff = _dragStartPoint - currentPoint;

        // Check if the mouse has moved enough to start a drag operation
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (_isDragging) return;
            _isDragging = true;

            try
            {
                // At this point, SelectedItems contains the correct items:
                // - If we clicked on an already-selected item, we prevented WPF
                //   from changing the selection, so all selected items are preserved.
                // - If we clicked on a non-selected item, WPF updated the selection
                //   to contain only that item (standard behavior).
                var entriesToDrag = ArchiveListView.SelectedItems.Cast<ArchiveEntry>().ToList();

                if (entriesToDrag.Count == 0)
                {
                    _isDragging = false;
                    return;
                }

                // Create a temporary directory for extraction
                var tempDir = Path.Combine(Path.GetTempPath(), "Winpress_DragDrop_" + Path.GetRandomFileName());
                Directory.CreateDirectory(tempDir);

                // Extract ALL selected files to the temp directory
                var extractedPaths = await ExtractSelectionToPathAsync(tempDir, entriesToDrag);

                if (extractedPaths.Count == 0)
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                    _isDragging = false;
                    return;
                }

                // Create a data object with the extracted file paths
                var dataObject = new DataObject(DataFormats.FileDrop, extractedPaths.ToArray());

                // Start the drag operation
                DragDrop.DoDragDrop(ArchiveListView, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
                
                // Clean up temp dir after drag completes
                try { Directory.Delete(tempDir, true); } catch { }
            }
            finally
            {
                _isDragging = false;
            }
        }
    }

    /// <summary>
    /// Walks up the visual tree to find the first ancestor of type T.
    /// Used to find the ListViewItem from a click on a child element.
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T result) return result;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>
    /// Extracts a specific list of entries to a destination path (bypasses SelectedEntries).
    /// Used for drag-and-drop to ensure all originally selected files are extracted,
    /// even if WPF resets the ListView selection during the mouse operation.
    /// </summary>
    private Task<List<string>> ExtractSelectionToPathAsync(string destination, List<ArchiveEntry> entries)
    {
        if (_vm.ArchivePath == null || entries.Count == 0 || _vm.ExtractFlatRequested == null)
            return Task.FromResult(new List<string>());

        return _vm.ExtractFlatRequested(_vm.ArchivePath, entries, destination);
    }

    private Task<List<string>> ExtractFlatAsync(string archivePath, List<ArchiveEntry> selectedEntries, string destination)
    {
        return _extractionService.ExtractFlatAsync(archivePath, selectedEntries, destination);
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
