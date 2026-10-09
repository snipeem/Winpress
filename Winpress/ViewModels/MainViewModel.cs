using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winpress.Models;
using Winpress.Services;

namespace Winpress.ViewModels;

/// <summary>
/// Main ViewModel — drives the archive browser window.
///
/// Lifecycle:
///   1. User opens an archive → <see cref="OpenArchiveAsync"/> → flat list loaded into _allEntries.
///   2. View shows <see cref="DisplayEntries"/> = direct children of <see cref="CurrentInternalPath"/>.
///   3. Double-click a folder → <see cref="NavigateIntoEntryAsync"/> updates CurrentInternalPath.
///   4. Double-click a nested archive → extract to temp, push old archive onto stack, reload.
///   5. Breadcrumb click / Back button → pop the navigation stack.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ArchiveService    _archiveService;
    private readonly ExtractionService _extractionService;

    // All entries flat-loaded from the current (outermost or nested) archive
    private List<ArchiveEntry> _allEntries = new();

    // Stack for nested archives: (archivePath, tempDir, internalPath, all entries)
    private readonly Stack<(string ArchivePath, string? TempDir, string InternalPath, List<ArchiveEntry> Entries)>
        _nestedStack = new();

    // Current sort state
    private string _sortColumn    = "Name";
    private bool   _sortAscending = true;

    // ── Observable state ─────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<ArchiveEntry> _displayEntries = new();
    [ObservableProperty] private ObservableCollection<BreadcrumbItem> _breadcrumbs  = new();
    [ObservableProperty] private List<ArchiveEntry> _selectedEntries                = new();

    [ObservableProperty] private string? _archivePath;
    [ObservableProperty] private string? _archiveName;
    [ObservableProperty] private string  _currentInternalPath = string.Empty;

    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private bool    _hasArchive;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string  _statusText = "Open an archive to get started.";
    [ObservableProperty] private string  _searchText = string.Empty;

    // For the "Register Explorer context menus" toggle in the menu
    [ObservableProperty] private bool _shellIntegrationEnabled = ShellIntegrationService.IsRegistered();

    // ── Events (raised for UI-layer dialogs that need a WPF Window) ───────────

    /// Raised when an encrypted archive needs a password.  Args = archive path.
    public event Func<string, Task<string?>>? PasswordRequested;

    /// Raised when the extraction needs a destination folder.
    public event Func<string, Task<string?>>? FolderPickerRequested;

    /// Raised to show the progress window for a running extraction.
    public event Action<ExtractionProgressViewModel>? ShowProgressWindow;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel(ArchiveService archiveService, ExtractionService extractionService)
    {
        _archiveService    = archiveService;
        _extractionService = extractionService;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  COMMANDS
    // ══════════════════════════════════════════════════════════════════════════

    // ── Open ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task OpenArchiveAsync(string? path = null)
    {
        if (path == null)
        {
            // The view code-behind subscribes to this event and shows an OpenFileDialog.
            // We re-use FolderPickerRequested but the caller shows a file picker instead.
            // Actually, the file-open dialog is triggered from MainWindow directly.
            return;
        }

        if (!File.Exists(path)) return;

        await LoadArchiveInternalAsync(path);
    }

    /// <summary>
    /// Opens an archive with a known password.  Called by the view after the
    /// password dialog returns a non-null value.
    /// </summary>
    [RelayCommand]
    public async Task OpenArchiveWithPasswordAsync((string Path, string Password) args) =>
        await LoadArchiveInternalAsync(args.Path, args.Password);

    // ── Navigate ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task NavigateIntoEntryAsync(ArchiveEntry? entry)
    {
        if (entry == null) return;

        if (entry.IsDirectory)
        {
            CurrentInternalPath = entry.FullPath;
            RefreshDisplayEntries();
            UpdateBreadcrumbs();
        }
        else if (entry.IsNestedArchive)
        {
            await OpenNestedArchiveAsync(entry);
        }
        else
        {
            // Regular file: extract to temp and open with default app
            await OpenFilePreviewAsync(entry);
        }
    }

    [RelayCommand]
    public void NavigateToBreadcrumb(BreadcrumbItem? item)
    {
        if (item == null) return;
        CurrentInternalPath = item.Path;
        RefreshDisplayEntries();
        UpdateBreadcrumbs();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    public void NavigateBack()
    {
        if (!string.IsNullOrEmpty(CurrentInternalPath))
        {
            // Go up one folder inside the current archive
            var slash = CurrentInternalPath.LastIndexOf('/');
            CurrentInternalPath = slash > 0 ? CurrentInternalPath[..slash] : string.Empty;
            RefreshDisplayEntries();
            UpdateBreadcrumbs();
        }
        else if (_nestedStack.Count > 0)
        {
            // Exit the nested archive
            var (archivePath, tempDir, internalPath, entries) = _nestedStack.Pop();
            ArchivePath         = archivePath;
            ArchiveName         = Path.GetFileName(archivePath);
            _allEntries         = entries;
            CurrentInternalPath = internalPath;
            RefreshDisplayEntries();
            UpdateBreadcrumbs();

            if (tempDir != null)
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        NavigateBackCommand.NotifyCanExecuteChanged();
    }

    private bool CanNavigateBack() =>
        !string.IsNullOrEmpty(CurrentInternalPath) || _nestedStack.Count > 0;

    // ── Extract ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasArchive))]
    public async Task ExtractAllAsync()
    {
        if (ArchivePath == null) return;
        var dest = FolderPickerRequested is not null
            ? await FolderPickerRequested("Choose extraction destination")
            : null;
        if (dest == null) return;
        await RunExtractionAsync(dest, extractAll: true);
    }

    [RelayCommand(CanExecute = nameof(CanExtractSelected))]
    public async Task ExtractSelectedAsync()
    {
        if (ArchivePath == null || SelectedEntries.Count == 0) return;
        var dest = FolderPickerRequested is not null
            ? await FolderPickerRequested("Extract selected files to\u2026")
            : null;
        if (dest == null) return;
        await RunExtractionAsync(dest, extractAll: false);
    }

    private bool CanExtractSelected() => HasArchive && SelectedEntries.Count > 0;

    /// <summary>
    /// "Extract Here" — extract the archive next to itself.
    /// Called from the shell context menu command-line.
    /// </summary>
    [RelayCommand]
    public async Task ExtractHereAsync(string? archivePath)
    {
        if (archivePath == null || !File.Exists(archivePath)) return;
        var dest = Path.GetDirectoryName(archivePath) ?? ".";
        ArchivePath = archivePath;
        await RunExtractionAsync(dest, extractAll: true);
    }

    /// <summary>
    /// "Extract to Path" — called from the shell context menu with an explicit destination.
    /// </summary>
    [RelayCommand]
    public async Task ExtractToPathAsync((string Archive, string Destination) args)
    {
        ArchivePath = args.Archive;
        await RunExtractionAsync(args.Destination, extractAll: true);
    }

    // ── Search / filter ───────────────────────────────────────────────────────

    partial void OnSearchTextChanged(string value)
    {
        RefreshDisplayEntries();
    }

    // ── Sort ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    public void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn    = column;
            _sortAscending = true;
        }
        RefreshDisplayEntries();
    }

    // ── Shell integration ─────────────────────────────────────────────────────

    [RelayCommand]
    public void ToggleShellIntegration()
    {
        if (ShellIntegrationEnabled)
            ShellIntegrationService.Unregister();
        else
            ShellIntegrationService.Register();

        ShellIntegrationEnabled = ShellIntegrationService.IsRegistered();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  INTERNAL HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private async Task LoadArchiveInternalAsync(string path, string? password = null)
    {
        IsLoading    = true;
        ErrorMessage = null;
        StatusText   = "Opening archive…";

        try
        {
            var result = await _archiveService.OpenAsync(path, password);

            if (result.NeedsPassword)
            {
                if (PasswordRequested != null)
                {
                    var pwd = await PasswordRequested(path);
                    if (pwd != null)
                    {
                        await LoadArchiveInternalAsync(path, pwd);
                        return;
                    }
                }
                StatusText   = "Archive requires a password.";
                ErrorMessage = "This archive is password-protected.";
                return;
            }

            if (result.Entries == null)
            {
                ErrorMessage = result.ErrorMessage ?? "Could not open the archive.";
                StatusText   = ErrorMessage;
                return;
            }

            _allEntries         = result.Entries;
            _nestedStack.Clear();
            ArchivePath         = path;
            ArchiveName         = Path.GetFileName(path);
            CurrentInternalPath = string.Empty;
            HasArchive          = true;

            RefreshDisplayEntries();
            UpdateBreadcrumbs();
        }
        finally
        {
            IsLoading = false;
            NavigateBackCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task OpenNestedArchiveAsync(ArchiveEntry entry)
    {
        if (ArchivePath == null) return;

        StatusText = $"Opening {entry.Name}…";
        IsLoading  = true;
        try
        {
            var tempDir  = Path.Combine(Path.GetTempPath(), "Winpress_" + Path.GetRandomFileName());
            var extracted = await _extractionService.ExtractToTempAsync(ArchivePath, entry);

            if (extracted == null)
            {
                StatusText = $"Could not extract {entry.Name}.";
                return;
            }

            // Push current state
            _nestedStack.Push((ArchivePath!, null, CurrentInternalPath, _allEntries));

            // Load the inner archive
            await LoadArchiveInternalAsync(extracted);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OpenFilePreviewAsync(ArchiveEntry entry)
    {
        if (ArchivePath == null) return;
        StatusText = $"Extracting {entry.Name} for preview…";

        var tempPath = await _extractionService.ExtractToTempAsync(ArchivePath, entry);
        if (tempPath != null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = tempPath,
                    UseShellExecute = true
                });
            }
            catch
            {
                StatusText = $"No application is associated with {entry.Extension} files.";
            }
        }

        StatusText = string.Empty;
    }

    private async Task RunExtractionAsync(string destination, bool extractAll)
    {
        if (ArchivePath == null) return;

        var progressVm = new ExtractionProgressViewModel(ArchiveName ?? "archive");
        ShowProgressWindow?.Invoke(progressVm);

        var progress = new Progress<ExtractionProgress>(p =>
        {
            progressVm.CurrentFile    = p.CurrentFile;
            progressVm.FilesCompleted = p.FilesCompleted;
            progressVm.TotalFiles     = p.TotalFiles;
            progressVm.Fraction       = p.Fraction;
        });

        try
        {
            if (extractAll)
                await _extractionService.ExtractAllAsync(ArchivePath, destination, progress: progress,
                    ct: progressVm.CancellationToken);
            else
                await _extractionService.ExtractSelectedAsync(ArchivePath, SelectedEntries, destination,
                    progress: progress, ct: progressVm.CancellationToken);

            progressVm.IsComplete = true;
            StatusText = $"Extracted to {destination}";
        }
        catch (OperationCanceledException)
        {
            progressVm.IsCancelled = true;
            StatusText = "Extraction cancelled.";
        }
        catch (Exception ex)
        {
            progressVm.ErrorMessage = ex.Message;
            StatusText = $"Extraction failed: {ex.Message}";
        }
    }

    // ── Display entries filtering / sorting ───────────────────────────────────

    private void RefreshDisplayEntries()
    {
        var raw = GetDirectChildren(CurrentInternalPath);

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            raw = raw.Where(e => e.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Sort
        raw = ApplySort(raw);

        DisplayEntries = new ObservableCollection<ArchiveEntry>(raw);
        UpdateStatusText();
    }

    private List<ArchiveEntry> GetDirectChildren(string parentPath)
    {
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ArchiveEntry>();

        foreach (var entry in _allEntries)
        {
            var path = entry.FullPath;

            if (!string.IsNullOrEmpty(parentPath))
            {
                var prefix = parentPath + "/";
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                path = path[prefix.Length..];
            }

            // Skip the entry if it's deeper than one level
            var slashIdx = path.IndexOf('/');
            if (slashIdx >= 0)
            {
                // It lives in a subfolder — add a synthetic directory for the top segment
                var segName = path[..slashIdx];
                var segPath = string.IsNullOrEmpty(parentPath) ? segName : $"{parentPath}/{segName}";

                if (seen.Add(segPath))
                {
                    // Try to find a real directory entry, otherwise create a virtual one
                    var realDir = _allEntries.FirstOrDefault(e =>
                        e.IsDirectory &&
                        e.FullPath.Equals(segPath, StringComparison.OrdinalIgnoreCase));

                    result.Add(realDir ?? new ArchiveEntry
                    {
                        Name        = segName,
                        FullPath    = segPath,
                        IsDirectory = true,
                        IsVirtual   = true
                    });
                }
            }
            else if (!string.IsNullOrEmpty(path) && seen.Add(entry.FullPath))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private List<ArchiveEntry> ApplySort(List<ArchiveEntry> entries)
    {
        // Folders always come before files
        IOrderedEnumerable<ArchiveEntry> ordered;

        Func<ArchiveEntry, object?> key = _sortColumn switch
        {
            "Size"     => e => e.Size,
            "Packed"   => e => e.CompressedSize,
            "Ratio"    => e => e.Size > 0 ? (double)e.CompressedSize / e.Size : 0.0,
            "Modified" => e => e.LastModified,
            "Type"     => e => e.TypeDisplay,
            _          => e => e.Name   // default: Name
        };

        if (_sortAscending)
            ordered = entries.OrderBy(e => !e.IsDirectory).ThenBy(key);
        else
            ordered = entries.OrderBy(e => !e.IsDirectory).ThenByDescending(key);

        return ordered.ToList();
    }

    private void UpdateBreadcrumbs()
    {
        var items = new ObservableCollection<BreadcrumbItem>
        {
            new BreadcrumbItem(ArchiveName ?? "Archive", string.Empty)
        };

        if (!string.IsNullOrEmpty(CurrentInternalPath))
        {
            var parts      = CurrentInternalPath.Split('/');
            var accumulated = string.Empty;
            foreach (var part in parts)
            {
                accumulated = string.IsNullOrEmpty(accumulated) ? part : $"{accumulated}/{part}";
                items.Add(new BreadcrumbItem(part, accumulated));
            }
        }

        Breadcrumbs = items;
    }

    private void UpdateStatusText()
    {
        var files = DisplayEntries.Count(e => !e.IsDirectory);
        var dirs  = DisplayEntries.Count(e =>  e.IsDirectory);
        var sel   = SelectedEntries.Count;

        StatusText = sel > 0
            ? $"{sel} selected  ·  {files} file{Pl(files)}, {dirs} folder{Pl(dirs)}"
            : $"{files} file{Pl(files)}, {dirs} folder{Pl(dirs)}";
    }

    private static string Pl(int n) => n == 1 ? string.Empty : "s";
}

// ── Tiny VM for the progress window ──────────────────────────────────────────

public partial class ExtractionProgressViewModel : ObservableObject
{
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty] private string  _archiveName;
    [ObservableProperty] private string  _currentFile    = string.Empty;
    [ObservableProperty] private int     _filesCompleted;
    [ObservableProperty] private int     _totalFiles;
    [ObservableProperty] private double  _fraction;
    [ObservableProperty] private bool    _isComplete;
    [ObservableProperty] private bool    _isCancelled;
    [ObservableProperty] private string? _errorMessage;

    public CancellationToken CancellationToken => _cts.Token;

    public ExtractionProgressViewModel(string archiveName) =>
        ArchiveName = archiveName;

    [RelayCommand]
    public void Cancel() => _cts.Cancel();
}
