using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Winpress.Models;
using Winpress.ViewModels;
using Winpress.Views;

// Explicit aliases — prevents ambiguity with System.Windows.Forms when UseWindowsForms=true
// (Most are now defined globally in GlobalUsings.cs)
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MouseButtons = System.Windows.Input.MouseButtonState;

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
        _vm.PasswordRequested       = ShowPasswordDialogAsync;
        _vm.FolderPickerRequested   = ShowFolderPickerAsync;
        _vm.ShowProgressWindow      = ShowProgressWindow;
        _vm.ExtractFlatRequested    = ExtractFlatAsync;
        _vm.CreateArchiveRequested  = ShowCreateArchiveDialogAsync;

        // Wire up mouse events for drag-and-drop and marquee selection
        ArchiveListView.PreviewMouseLeftButtonDown += ArchiveListView_PreviewMouseLeftButtonDown;
        ArchiveListView.PreviewMouseLeftButtonUp += ArchiveListView_PreviewMouseLeftButtonUp;
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

    private void MenuNewArchive_Click(object sender, RoutedEventArgs e)
    {
        _vm.CreateArchiveCommand.Execute(null);
    }

    private Task<Winpress.Views.CreateArchiveResult?> ShowCreateArchiveDialogAsync(
        (IReadOnlyList<string> Sources, string? SuggestedPath, string Description) args)
    {
        var dialog = new Winpress.Views.CreateArchiveDialog(
            suggestedOutputPath: args.SuggestedPath,
            sourceDescription:   args.Description,
            initialSources:      args.Sources)
        {
            Owner = this
        };
        var shown = dialog.ShowDialog();
        return Task.FromResult(shown == true ? dialog.Result : null);
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

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

    // ── Drag & drop (file into window) ───────────────────────────────────────

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

    // ═══════════════════════════════════════════════════════════════════════
    //  DRAG-AND-DROP (from archive) + MARQUEE (rubber-band) SELECTION
    //
    //  Strategy:
    //   • PreviewMouseLeftButtonDown — record the mouse position and take a
    //     snapshot of the current selection.  If the click lands on empty
    //     space, start a marquee and swallow the event; otherwise let WPF
    //     handle selection naturally (this preserves double-click, unselect
    //     on empty space, and normal click-to-select).
    //   • MouseMove — if a marquee is active, update the rubber-band rectangle
    //     and select intersecting items; otherwise, when the drag threshold is
    //     exceeded, use the snapshot to start a shell drag operation.
    //   • PreviewMouseLeftButtonUp — tear down the marquee if active.
    // ═══════════════════════════════════════════════════════════════════════

    private Point _mouseDownPoint;
    private bool _isDragging = false;
    private List<ArchiveEntry>? _dragSelectionSnapshot;

    // Marquee state
    private bool _isMarqueeActive = false;
    private Point _marqueeStartPoint;
    private MarqueeAdorner? _marqueeAdorner;
    private AdornerLayer? _adornerLayer;

    private void ArchiveListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDownPoint = e.GetPosition(null);
        _isDragging = false;

        // Snapshot the current selection BEFORE WPF processes the click.
        // This guarantees that drag-and-drop uses the selection at click time,
        // not whatever WPF may change it to during event bubbling.
        _dragSelectionSnapshot = ArchiveListView.SelectedItems.Cast<ArchiveEntry>().ToList();

        // Hit-test: is the click on an item or on empty space?
        var clickedElement = e.OriginalSource as DependencyObject;
        var listViewItem = FindAncestor<ListViewItem>(clickedElement);

        if (listViewItem == null)
        {
            // ── Empty-space click → start marquee selection ────────────────
            // Clear the existing selection unless Ctrl is held (additive mode).
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                ArchiveListView.SelectedItems.Clear();
            }

            StartMarquee(e.GetPosition(ArchiveListView));
            ArchiveListView.CaptureMouse();
            e.Handled = true;
        }
        // If clicked on an item we intentionally DO NOT set Handled=true.
        // This allows WPF to handle:
        //   • Normal click-to-select (single item)
        //   • Ctrl+click toggle
        //   • Shift+click range
        //   • Double-click → MouseDoubleClick event (opens entry / nested archive)
        //   • Click on empty space inside the ListView to clear the selection
        // Setting Handled here would suppress all of those, which was the root
        // cause of the previous bugs (double-click broken, couldn't unselect).
    }

    private void ArchiveListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isMarqueeActive)
        {
            EndMarquee();
            ArchiveListView.ReleaseMouseCapture();
            e.Handled = true;
        }
        _dragSelectionSnapshot = null;
    }

    private async void ArchiveListView_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentPoint = e.GetPosition(null);
        var diff = _mouseDownPoint - currentPoint;

        // ── Marquee update path ──────────────────────────────────────────
        if (_isMarqueeActive)
        {
            UpdateMarquee(e.GetPosition(ArchiveListView));
            return;
        }

        // ── Drag-and-drop path ─────────────────────────────────────────
        if (_dragSelectionSnapshot == null || _dragSelectionSnapshot.Count == 0)
            return;

        // Check if the mouse has moved enough to start a drag operation
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (_isDragging) return;
            _isDragging = true;

            try
            {
                var entriesToDrag = _dragSelectionSnapshot;

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

                // Start the drag operation (blocks until drop or cancel)
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

    // ── Marquee helpers ─────────────────────────────────────────────────────

    private void StartMarquee(Point startPoint)
    {
        _isMarqueeActive = true;
        _marqueeStartPoint = startPoint;

        _adornerLayer = AdornerLayer.GetAdornerLayer(ArchiveListView);
        if (_adornerLayer != null)
        {
            _marqueeAdorner = new MarqueeAdorner(ArchiveListView, startPoint, startPoint);
            _adornerLayer.Add(_marqueeAdorner);
        }
    }

    private void UpdateMarquee(Point currentPoint)
    {
        if (_marqueeAdorner != null)
        {
            _marqueeAdorner.UpdateRectangle(_marqueeStartPoint, currentPoint);
            UpdateMarqueeSelection(_marqueeStartPoint, currentPoint);
        }
    }

    private void EndMarquee()
    {
        _isMarqueeActive = false;
        if (_adornerLayer != null && _marqueeAdorner != null)
        {
            _adornerLayer.Remove(_marqueeAdorner);
            _marqueeAdorner = null;
        }
    }

    /// <summary>
    /// Selects all realised (visible) ListViewItems whose bounds intersect the
    /// marquee rectangle.  Virtualised items that aren't currently in the visual
    /// tree are skipped — acceptable for typical archive sizes.
    /// </summary>
    private void UpdateMarqueeSelection(Point start, Point end)
    {
        var marqueeRect = new Rect(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Abs(end.X - start.X),
            Math.Abs(end.Y - start.Y));

        for (int i = 0; i < ArchiveListView.Items.Count; i++)
        {
            var container = ArchiveListView.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
            if (container == null) continue; // Virtualised — not currently realised

            var itemBounds = VisualTreeHelper.GetDescendantBounds(container);
            var itemTopLeft = container.TranslatePoint(new Point(0, 0), ArchiveListView);
            var itemRect = new Rect(itemTopLeft.X, itemTopLeft.Y, itemBounds.Width, itemBounds.Height);

            if (marqueeRect.IntersectsWith(itemRect))
            {
                container.IsSelected = Keyboard.Modifiers == ModifierKeys.Control
                    ? !container.IsSelected   // Ctrl: toggle
                    : true;                    // Normal: select
            }
            else if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                container.IsSelected = false;
            }
        }
    }

    // ── Visual tree helper ──────────────────────────────────────────────────

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T result) return result;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // ── Extraction helpers for drag-and-drop ─────────────────────────────────

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

    private Task<string?> ShowPasswordDialogAsync((string Path, bool IsRetry) args)
    {
        var dialog = new PasswordDialog(args.IsRetry) { Owner = this };
        return Task.FromResult(
            dialog.ShowDialog() == true ? dialog.Password : null);
    }

    private Task<string?> ShowFolderPickerAsync(string prompt)
    {
        // .NET 10 ships System.Windows.Forms; use the WinForms FolderBrowserDialog
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = prompt,
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
            Title = "Open Archive",
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
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MARQUEE ADORNER — draws the semi-transparent blue rubber-band
    //  selection rectangle on top of the ListView.
    // ═══════════════════════════════════════════════════════════════════════

    private class MarqueeAdorner : Adorner
    {
        private Point _startPoint;
        private Point _endPoint;
        private readonly Pen _pen;
        private readonly SolidColorBrush _fill;

        public MarqueeAdorner(UIElement adornedElement, Point start, Point end)
            : base(adornedElement)
        {
            _startPoint = start;
            _endPoint = end;

            // Windows 11 selection blue (#2B7FD4) with transparency
            _pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 43, 127, 212)), 1);
            _pen.Freeze();
            _fill = new SolidColorBrush(Color.FromArgb(40, 43, 127, 212));
            _fill.Freeze();

            IsHitTestVisible = false;
        }

        public void UpdateRectangle(Point start, Point end)
        {
            _startPoint = start;
            _endPoint = end;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(
                Math.Min(_startPoint.X, _endPoint.X),
                Math.Min(_startPoint.Y, _endPoint.Y),
                Math.Abs(_endPoint.X - _startPoint.X),
                Math.Abs(_endPoint.Y - _startPoint.Y));

            drawingContext.DrawRectangle(_fill, _pen, rect);
            base.OnRender(drawingContext);
        }
    }
}
