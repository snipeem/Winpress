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
    private readonly ArchiveService     _archiveService;
    private readonly ExtractionService  _extractionService;
    private readonly CompressionService _compressionService;

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

    // For the "Set as Default" toggle in the menu
    [ObservableProperty] private bool _fileAssociationEnabled = FileAssociationService.IsRegistered();

    // ── Callbacks (set by the UI layer for dialogs that need a WPF Window) ─────

    /// <summary>
    /// Called when an encrypted archive needs a password.
    /// Args = (archive path, isRetry).
    ///   isRetry=true means the previous attempt was wrong — the dialog shows a
    ///   red "Incorrect password" hint.
    /// Returns null when the user cancels.
    /// </summary>
    public Func<(string Path, bool IsRetry), Task<string?>>? PasswordRequested { get; set; }

    /// Called when the extraction needs a destination folder.
    public Func<string, Task<string?>>? FolderPickerRequested { get; set; }

    /// Called to show the progress window for a running extraction.
    public Action<ExtractionProgressViewModel>? ShowProgressWindow { get; set; }

    /// <summary>
    /// Called to extract selected files to a specific path (used for drag-and-drop).
    /// Args = (archivePath, selectedEntries, destinationPath).
    /// Returns the list of extracted file/folder paths.
    /// </summary>
    public Func<string, List<ArchiveEntry>, string, Task<List<string>>>? ExtractFlatRequested { get; set; }

    // ── Compression callbacks (set by the UI layer) ───────────────────────────

    /// <summary>
    /// Called when the user triggers "New Archive" or "Add to Archive…".
    /// Receives (sourcePaths, suggestedOutputPath, descriptionLabel).
    /// Returns a <see cref="CreateArchiveResult"/> or null if the user cancelled.
    /// </summary>
    public Func<(IReadOnlyList<string> Sources, string? SuggestedPath, string Description),
                Task<Winpress.Views.CreateArchiveResult?>>? CreateArchiveRequested { get; set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    // ── Session password memory ────────────────────────────────────────────
    // Remembers the last successfully-used password for the current session
    // so the user isn't re-prompted for the same archive.
    private string? _sessionPassword;

    public MainViewModel(
        ArchiveService     archiveService,
        ExtractionService  extractionService,
        CompressionService compressionService)
    {
        _archiveService     = archiveService;
        _extractionService  = extractionService;
        _compressionService = compressionService;
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
            NavigateBackCommand.NotifyCanExecuteChanged();
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
        NavigateBackCommand.NotifyCanExecuteChanged();
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
            ? await FolderPickerRequested("Extract selected files to…")
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

    /// <summary>
    /// Extracts selected entries to a specific path (used for drag-and-drop).
    /// Returns the list of extracted file/folder paths.
    /// </summary>
    public async Task<List<string>> ExtractSelectedToPathAsync(string destination)
    {
        if (ArchivePath == null || SelectedEntries.Count == 0)
            return new List<string>();

        // Call the service directly so _sessionPassword is forwarded —
        // the ExtractFlatRequested callback has no password parameter.
        return await _extractionService.ExtractFlatAsync(
            ArchivePath, SelectedEntries, destination, password: _sessionPassword);
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
        {
            ShellIntegrationService.Unregister();
            StatusText = "Explorer context menus removed.";
        }
        else
        {
            ShellIntegrationService.Register();
            StatusText = "Explorer context menus registered. Right-click any file to see Winpress options.";
        }

        ShellIntegrationEnabled = ShellIntegrationService.IsRegistered();
    }

    // ── File association (default app) ───────────────────────────────────────

    [RelayCommand]
    public void SetAsDefault()
    {
        // Register Winpress as a valid handler in the registry, then open
        // Windows Default Apps settings so the user can select it.
        FileAssociationService.RegisterAndOpenSettings();
        FileAssociationEnabled = FileAssociationService.IsRegistered();
    }

    [RelayCommand]
    public void RemoveDefault()
    {
        FileAssociationService.UnregisterCapabilities();
        FileAssociationEnabled = FileAssociationService.IsRegistered();
    }

    // ── Compression ───────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the Create Archive dialog. Accepts a list of source paths to pre-fill.
    /// Called from the File menu and from the --add-to-archive CLI arg.
    /// </summary>
    [RelayCommand]
    public async Task CreateArchiveAsync(IReadOnlyList<string>? sourcePaths = null)
    {
        if (CreateArchiveRequested == null) return;

        var sources   = sourcePaths ?? new List<string>();
        var suggested = sources.Count == 1
            ? Path.Combine(
                Path.GetDirectoryName(sources[0]) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.GetFileNameWithoutExtension(sources[0]) + ".zip")
            : null;

        var description = sources.Count switch
        {
            0 => "Select files or folders to compress",
            1 => $"Compressing: {Path.GetFileName(sources[0])}",
            _ => $"Compressing {sources.Count} items"
        };

        var result = await CreateArchiveRequested((sources, suggested, description));
        if (result == null) return;

        // Use the source paths from the dialog result (user may have added/removed files)
        var finalSources = result.SourcePaths.Count > 0 ? result.SourcePaths : sources;

        await RunCompressionAsync(
            finalSources,
            result.OutputPath,
            new CompressionOptions(result.Format, result.Password, result.Level));
    }

    /// <summary>
    /// Silent one-click compression from the shell context menu.
    /// Args = (shortFormatLabel e.g. "zip", sourcePath).
    /// Output is placed next to the source with the appropriate extension.
    /// </summary>
    [RelayCommand]
    public async Task CompressHereAsync((string Format, string SourcePath) args)
    {
        var fmt = Winpress.Models.CompressionFormatExtensions.FromShortLabel(args.Format);
        var ext = fmt.ToExtension();

        var dir      = Path.GetDirectoryName(args.SourcePath)
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var stem     = Path.GetFileNameWithoutExtension(args.SourcePath);
        var outPath  = Path.Combine(dir, stem + ext);

        // Avoid overwriting: append (1), (2) … until the path is free.
        var i = 1;
        while (File.Exists(outPath))
            outPath = Path.Combine(dir, $"{stem} ({i++}){ext}");

        await RunCompressionAsync(
            new List<string> { args.SourcePath },
            outPath,
            new CompressionOptions(fmt));
    }

    // ── Internal compression runner ───────────────────────────────────────────

    private async Task RunCompressionAsync(
        IReadOnlyList<string> sources,
        string                outputPath,
        CompressionOptions    opts)
    {
        var archiveName = Path.GetFileName(outputPath);
        var progressVm  = new ExtractionProgressViewModel(archiveName, isCompressing: true);
        ShowProgressWindow?.Invoke(progressVm);

        var progressReporter = new Progress<(int Completed, int Total, string CurrentFile)>(t =>
        {
            progressVm.FilesCompleted = t.Completed;
            progressVm.TotalFiles     = t.Total;
            progressVm.CurrentFile    = t.CurrentFile;
            progressVm.Fraction       = t.Total > 0 ? (double)t.Completed / t.Total : 0;
        });

        try
        {
            await _compressionService.CompressAsync(
                sources, outputPath, opts,
                progressReporter,
                progressVm.CancellationToken);

            progressVm.FilesCompleted = progressVm.TotalFiles;
            progressVm.Fraction       = 1.0;
            progressVm.IsComplete     = true;
            StatusText = $"Created: {archiveName}";
        }
        catch (OperationCanceledException)
        {
            progressVm.IsCancelled = true;
        }
        catch (Exception ex)
        {
            progressVm.ErrorMessage = ex.Message;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  INTERNAL HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private async Task LoadArchiveInternalAsync(string path, string? password = null, bool clearStack = true)
    {
        IsLoading    = true;
        ErrorMessage = null;
        StatusText   = "Opening archive…";

        try
        {
            var result = await _archiveService.OpenAsync(path, password);

            // ── Password handling with session-memory ──────────────────────
            if (result.NeedsPassword)
            {
                // First, try the stored session password silently (only on first
                // attempt with no explicit password).
                if (password == null && _sessionPassword != null)
                {
                    var sessionResult = await _archiveService.OpenAsync(path, _sessionPassword);
                    if (!sessionResult.NeedsPassword && sessionResult.Entries != null)
                    {
                        result   = sessionResult;
                        password = _sessionPassword;
                    }
                    else
                    {
                        // Session password no longer valid — clear it.
                        _sessionPassword = null;
                    }
                }

                // Still needs a password? Prompt the user (with retry feedback).
                if (result.NeedsPassword)
                {
                    if (PasswordRequested != null)
                    {
                        bool isRetry = false;
                        while (true)
                        {
                            var pwd = await PasswordRequested((path, isRetry));
                            if (pwd == null) break;  // user cancelled

                            var tryResult = await _archiveService.OpenAsync(path, pwd);
                            if (!tryResult.NeedsPassword && tryResult.Entries != null)
                            {
                                _sessionPassword = pwd;
                                result           = tryResult;
                                password         = pwd;
                                break;
                            }
                            // Wrong password → loop again with retry feedback
                            isRetry = true;
                        }
                    }

                    if (result.NeedsPassword)
                    {
                        StatusText   = "Archive required a password.";
                        ErrorMessage = "This archive is password-protected.";
                        return;
                    }
                }
            }

            if (result.Entries == null)
            {
                ErrorMessage = result.ErrorMessage ?? "Could not open the archive.";
                StatusText   = ErrorMessage;
                return;
            }

            _allEntries         = result.Entries;
            if (clearStack)
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
            ExtractAllCommand.NotifyCanExecuteChanged();
            ExtractSelectedCommand.NotifyCanExecuteChanged();
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
            var extracted = await _extractionService.ExtractToTempAsync(ArchivePath, entry, password: _sessionPassword);

            if (extracted == null)
            {
                StatusText = $"Could not extract {entry.Name}.";
                return;
            }

            // Push current state
            _nestedStack.Push((ArchivePath!, null, CurrentInternalPath, _allEntries));

            // Load the inner archive (don't clear the stack — we just pushed to it)
            await LoadArchiveInternalAsync(extracted, clearStack: false);
        }
        finally
        {
            IsLoading = false;
            NavigateBackCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task OpenFilePreviewAsync(ArchiveEntry entry)
    {
        if (ArchivePath == null) return;
        StatusText = $"Extracting {entry.Name} for preview…";

        var tempPath = await _extractionService.ExtractToTempAsync(ArchivePath, entry, password: _sessionPassword);
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
                await _extractionService.ExtractAllAsync(ArchivePath, destination,
                    password: _sessionPassword,
                    progress: progress, ct: progressVm.CancellationToken);
            else
                await _extractionService.ExtractSelectedAsync(ArchivePath, SelectedEntries, destination,
                    password: _sessionPassword,
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

    // ── Property changed handlers to manually notify commands ─────────────────
    partial void OnSelectedEntriesChanged(List<ArchiveEntry> value)
    {
        ExtractSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentInternalPathChanged(string value)
    {
        NavigateBackCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasArchiveChanged(bool value)
    {
        ExtractAllCommand.NotifyCanExecuteChanged();
        ExtractSelectedCommand.NotifyCanExecuteChanged();
    }
}

// ── Tiny VM for the progress window ──────────────────────────────────────────

public partial class ExtractionProgressViewModel : ObservableObject
{
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty] private string  _archiveName;
    [ObservableProperty] private string  _currentFile      = string.Empty;
    [ObservableProperty] private int     _filesCompleted;
    [ObservableProperty] private int     _totalFiles;
    [ObservableProperty] private double  _fraction;
    [ObservableProperty] private bool    _isComplete;
    [ObservableProperty] private bool    _isCancelled;
    [ObservableProperty] private string? _errorMessage;

    // Context-aware labels — bound to ProgressWindow.xaml
    [ObservableProperty] private string _windowTitle      = "Extracting…";
    [ObservableProperty] private string _operationLabel   = "Extracting";
    [ObservableProperty] private string _completeMessage  = "Extraction complete!";
    [ObservableProperty] private string _cancelledMessage = "Extraction was cancelled.";

    public CancellationToken CancellationToken => _cts.Token;

    public ExtractionProgressViewModel(string archiveName, bool isCompressing = false)
    {
        _archiveName = archiveName;
        if (isCompressing)
        {
            _windowTitle      = "Compressing…";
            _operationLabel   = "Compressing";
            _completeMessage  = "Compression complete!";
            _cancelledMessage = "Compression was cancelled.";
        }
    }

    [RelayCommand]
    public void Cancel() => _cts.Cancel();
}
