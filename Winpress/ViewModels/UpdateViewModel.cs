using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winpress.Services;

namespace Winpress.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _service;
    private          string?       _downloadedPath;

    // ── Bound properties ──────────────────────────────────────────────────────

    [ObservableProperty] private string  _currentVersion  = UpdateService.CurrentVersion;
    [ObservableProperty] private string  _newVersion      = string.Empty;
    [ObservableProperty] private string  _changelog       = string.Empty;
    [ObservableProperty] private string  _statusText      = string.Empty;
    [ObservableProperty] private double  _downloadFraction;

    [ObservableProperty] private bool _isIdle        = true;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isReadyToApply;
    [ObservableProperty] private bool _hasFailed;

    public UpdateInfo? Info { get; private set; }

    // ── Construction ──────────────────────────────────────────────────────────

    public UpdateViewModel(UpdateInfo info, UpdateService service)
    {
        _service   = service;
        Info       = info;
        NewVersion = info.Version;
        Changelog  = FormatChangelog(info.Changelog);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Downloads the update binary and then applies it.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task UpdateNowAsync()
    {
        IsIdle        = false;
        IsDownloading = true;
        StatusText    = "Downloading update…";

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadFraction = p;
                StatusText = $"Downloading… {p * 100:F0}%";
            });

            _downloadedPath = await _service.DownloadUpdateAsync(
                Info!.DownloadUrl, progress);

            IsDownloading  = false;
            IsReadyToApply = true;
            StatusText     = "Download complete. Click Apply to restart and update.";
        }
        catch (OperationCanceledException)
        {
            Reset("Update cancelled.");
        }
        catch (Exception ex)
        {
            HasFailed  = true;
            IsDownloading = false;
            StatusText = $"Download failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Replaces the running .exe and restarts the app.
    /// Only available after a successful download.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsReadyToApply))]
    private void ApplyUpdate()
    {
        if (_downloadedPath is null) return;
        StatusText = "Applying update and restarting…";
        _service.ApplyUpdate(_downloadedPath);
    }

    [RelayCommand]
    private void Dismiss() => CloseRequested?.Invoke();

    // ── Helpers ───────────────────────────────────────────────────────────────

    public event Action? CloseRequested;

    private void Reset(string message)
    {
        IsIdle        = true;
        IsDownloading = false;
        IsReadyToApply = false;
        StatusText    = message;
        DownloadFraction = 0;
    }

    private static string FormatChangelog(string raw)
    {
        // GitHub release bodies use Markdown — show as-is in the text box.
        return string.IsNullOrWhiteSpace(raw) ? "No changelog provided." : raw.Trim();
    }
}
