using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Winpress.Models;
using Winpress.Services;

namespace Winpress.Views;

/// <summary>Result returned when the user clicks Create.</summary>
public record CreateArchiveResult(
    string                 OutputPath,
    CompressionFormat      Format,
    string?                Password,
    ArchiveCompressionLevel Level
);

/// <summary>
/// Dialog for choosing archive name, format, compression level and
/// optional password before Winpress creates a new archive.
/// </summary>
public partial class CreateArchiveDialog : Window
{
    public CreateArchiveResult? Result { get; private set; }

    // Show/hide state for password and confirm fields
    private bool _showPassword = false;
    private bool _showConfirm  = false;

    // Strength levels
    private enum Strength { Empty, Weak, Fair, Strong }

    public CreateArchiveDialog(
        string? suggestedOutputPath = null,
        string? sourceDescription   = null)
    {
        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(suggestedOutputPath))
            OutputPathBox.Text = suggestedOutputPath;
        if (!string.IsNullOrWhiteSpace(sourceDescription))
            SourceLabel.Text = sourceDescription;

        UpdatePanelVisibility();
        UpdateStrengthIndicator();
        UpdateAesBadge();
    }

    // ── Interaction ──────────────────────────────────────────────────────────

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var fmt  = GetSelectedFormat();
        var ext  = fmt.ToExtension();
        var dir  = Path.GetDirectoryName(OutputPathBox.Text) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var name = StripArchiveExtension(Path.GetFileNameWithoutExtension(
                       Path.GetFileName(OutputPathBox.Text)) ?? string.Empty);

        var dlg = new SaveFileDialog
        {
            Title            = "Choose output location",
            FileName         = string.IsNullOrEmpty(name) ? "archive" : name,
            DefaultExt       = ext,
            Filter           = BuildFilter(fmt),
            InitialDirectory = (dir != null && Directory.Exists(dir))
                               ? dir
                               : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (dlg.ShowDialog(this) == true)
            OutputPathBox.Text = dlg.FileName;
    }

    private void FormatCombo_SelectionChanged(object sender,
        SelectionChangedEventArgs e)
    {
        UpdatePanelVisibility();
        UpdateOutputExtension();
        UpdateAesBadge();
    }

    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        if (_showPassword) return; // TextBox is the active field

        // Mirror into the TextBox if user is currently seeing the plain text
        if (ConfirmPanel != null)
            ConfirmPanel.Visibility = PasswordBox.Password.Length > 0
                ? Visibility.Visible : Visibility.Collapsed;

        UpdateStrengthIndicator();
        UpdateAesBadge();

        if (ConfirmPanel.Visibility == Visibility.Visible && ConfirmBox != null)
        {
            ConfirmBox.Focus();
        }
    }

    private void PasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_showPassword) return;
        // Mirror back into the PasswordBox (keeps the model in sync)
        PasswordBox.Password = PasswordTextBox.Text;
        UpdateStrengthIndicator();
        UpdateAesBadge();
    }

    private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _showPassword = !_showPassword;

        if (_showPassword)
        {
            PasswordTextBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordTextBox.Visibility = Visibility.Visible;
            ((TextBlock)ShowPwdButton.Content).Text = "\uE7B4"; // hide eye
            ShowPwdButton.ToolTip = "Hide password";
        }
        else
        {
            PasswordBox.Password = PasswordTextBox.Text;
            PasswordTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            ((TextBlock)ShowPwdButton.Content).Text = "\uE7B3"; // show eye
            ShowPwdButton.ToolTip = "Show password";
        }
    }

    private void ToggleConfirmVisibility_Click(object sender, RoutedEventArgs e)
    {
        _showConfirm = !_showConfirm;

        if (_showConfirm)
        {
            ConfirmTextBox.Text = ConfirmBox.Password;
            ConfirmBox.Visibility = Visibility.Collapsed;
            ConfirmTextBox.Visibility = Visibility.Visible;
            ((TextBlock)ShowConfirmButton.Content).Text = "\uE7B4";
            ShowConfirmButton.ToolTip = "Hide password";
        }
        else
        {
            ConfirmBox.Password = ConfirmTextBox.Text;
            ConfirmTextBox.Visibility = Visibility.Collapsed;
            ConfirmBox.Visibility = Visibility.Visible;
            ((TextBlock)ShowConfirmButton.Content).Text = "\uE7B3";
            ShowConfirmButton.ToolTip = "Show password";
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorLabel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(OutputPathBox.Text))
        {
            ShowError("Please specify an output file path.");
            return;
        }

        var fmt = GetSelectedFormat();
        var pwd = GetCurrentPassword();

        if (fmt.SupportsPassword() && pwd.Length > 0
            && pwd != GetConfirmPassword())
        {
            ShowError("Passwords do not match.");
            return;
        }

        var level = ((LevelCombo.SelectedItem as ComboBoxItem)
                        ?.Tag as string) switch
        {
            "Fast" => ArchiveCompressionLevel.Fast,
            "Best" => ArchiveCompressionLevel.Best,
            _      => ArchiveCompressionLevel.Default
        };

        Result = new CreateArchiveResult(
            OutputPath : OutputPathBox.Text.Trim(),
            Format     : fmt,
            Password   : fmt.SupportsPassword() && pwd.Length > 0 ? pwd : null,
            Level      : level
        );

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetCurrentPassword() =>
        _showPassword ? PasswordTextBox.Text : PasswordBox.Password;

    private string GetConfirmPassword() =>
        _showConfirm ? ConfirmTextBox.Text : ConfirmBox.Password;

    private void UpdatePanelVisibility()
    {
        if (PasswordPanel == null) return;

        var supports = GetSelectedFormat().SupportsPassword();
        PasswordPanel.Visibility = supports ? Visibility.Visible : Visibility.Collapsed;

        if (ConfirmPanel != null && !supports)
            ConfirmPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateOutputExtension()
    {
        if (OutputPathBox == null) return;

        var current = OutputPathBox.Text;
        if (string.IsNullOrEmpty(current)) return;

        var dir     = Path.GetDirectoryName(current) ?? string.Empty;
        var stem    = StripArchiveExtension(Path.GetFileName(current) ?? string.Empty);
        var newExt  = GetSelectedFormat().ToExtension();
        OutputPathBox.Text = Path.Combine(dir, stem + newExt);
    }

    private CompressionFormat GetSelectedFormat() =>
        ((FormatCombo?.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "SevenZip"  => CompressionFormat.SevenZip,
            "TarGz"     => CompressionFormat.TarGz,
            "TarBz2"    => CompressionFormat.TarBz2,
            "TarXz"     => CompressionFormat.TarXz,
            "TarZst"    => CompressionFormat.TarZst,
            "Tar"       => CompressionFormat.Tar,
            "GZip"      => CompressionFormat.GZip,
            "BZip2"     => CompressionFormat.BZip2,
            "ZStandard" => CompressionFormat.ZStandard,
            _           => CompressionFormat.Zip
        };

    /// <summary>
    /// Pure C# password-strength estimate:
    ///   — count unique character classes (lower / upper / digit / symbol)
    ///   — multiply by log₂(charset size) × length
    /// No library required.
    /// </summary>
    private Strength EstimateStrength(string pwd)
    {
        if (string.IsNullOrEmpty(pwd)) return Strength.Empty;

        bool hasLower  = pwd.Any(char.IsLower);
        bool hasUpper  = pwd.Any(char.IsUpper);
        bool hasDigit  = pwd.Any(char.IsDigit);
        bool hasSymbol = pwd.Any(c => !char.IsLetterOrDigit(c));

        int classes = (hasLower ? 1 : 0) + (hasUpper ? 1 : 0)
                    + (hasDigit ? 1 : 0) + (hasSymbol ? 1 : 0);

        int charsetSize = (hasLower  ? 26 : 0) + (hasUpper ? 26 : 0)
                        + (hasDigit  ? 10 : 0) + (hasSymbol ? 32 : 0);
        if (charsetSize == 0) charsetSize = 1;

        double entropy = Math.Log2(charsetSize) * pwd.Length;

        // Thresholds tuned for a 2026 feel
        if (pwd.Length >= 12 && classes >= 4 && entropy >= 60) return Strength.Strong;
        if (pwd.Length >= 8  && classes >= 3 && entropy >= 40) return Strength.Fair;
        return Strength.Weak;
    }

    private void UpdateStrengthIndicator()
    {
        var pwd = GetCurrentPassword();
        var s   = EstimateStrength(pwd);

        if (s == Strength.Empty)
        {
            StrengthBar.Visibility = Visibility.Collapsed;
            return;
        }

        StrengthBar.Visibility = Visibility.Visible;
        StrengthBar.Background = s switch
        {
            Strength.Weak   => new SolidColorBrush(Color.FromRgb(229, 57, 53)),  // red
            Strength.Fair   => new SolidColorBrush(Color.FromRgb(245, 166, 35)), // amber
            Strength.Strong => new SolidColorBrush(Color.FromRgb(67, 160, 71)),  // green
            _               => new SolidColorBrush(Color.FromRgb(58, 58, 58))
        };
    }

    private void UpdateAesBadge()
    {
        if (AesBadgePanel == null) return;

        var fmt       = GetSelectedFormat();
        var hasPwd    = GetCurrentPassword().Length > 0;
        var supported = fmt.SupportsPassword();

        AesBadgePanel.Visibility = (hasPwd && supported)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string StripArchiveExtension(string path)
    {
        foreach (var ext in new[]
            { ".tar.gz",".tar.bz2",".tar.xz",".tar.zst",
              ".zip",".7z",".tar",".gz",".bz2",".xz",".zst" })
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return path[..^ext.Length];
        return path;
    }

    private static string BuildFilter(CompressionFormat f) => f switch
    {
        CompressionFormat.SevenZip  => "7-Zip archive|*.7z|All files|*.*",
        CompressionFormat.TarGz     => "TAR+GZip|*.tar.gz|All files|*.*",
        CompressionFormat.TarBz2    => "TAR+BZip2|*.tar.bz2|All files|*.*",
        CompressionFormat.TarXz     => "TAR+XZ|*.tar.xz|All files|*.*",
        CompressionFormat.TarZst    => "TAR+Zstandard|*.tar.zst|All files|*.*",
        CompressionFormat.Tar       => "TAR archive|*.tar|All files|*.*",
        CompressionFormat.GZip      => "GZip|*.gz|All files|*.*",
        CompressionFormat.BZip2     => "BZip2|*.bz2|All files|*.*",
        CompressionFormat.ZStandard => "Zstandard|*.zst|All files|*.*",
        _                           => "ZIP archive|*.zip|All files|*.*"
    };

    private void ShowError(string msg)
    {
        ErrorLabel.Text       = msg;
        ErrorLabel.Visibility = Visibility.Visible;
    }
}
