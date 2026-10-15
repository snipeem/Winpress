using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;

namespace Winpress.Views;

public partial class PasswordDialog : Window
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public string? Password { get; private set; }

    private bool _showPassword = false;

    private enum Strength { Empty, Weak, Fair, Strong }

    public PasswordDialog(bool isRetry = false)
    {
        InitializeComponent();

        // Show red error label when the user is re-prompted after a wrong attempt
        if (isRetry)
        {
            ErrorLabel.Text       = "Incorrect password — try again.";
            ErrorLabel.Foreground = new SolidColorBrush(Color.FromRgb(229, 57, 53));
            ErrorLabel.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => PasswordBox.Focus();
        UpdateStrengthIndicator();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        int trueValue = 1;
        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, sizeof(int));
    }

    private void OK_Click(object sender, RoutedEventArgs e)     => TryAccept();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  TryAccept();
        if (e.Key == Key.Escape) Close();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_showPassword) return;
        UpdateStrengthIndicator();
    }

    private void PasswordTextBox_TextChanged(object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_showPassword) return;
        PasswordBox.Password = PasswordTextBox.Text;
        UpdateStrengthIndicator();
    }

    private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _showPassword = !_showPassword;

        if (_showPassword)
        {
            PasswordTextBox.Text = PasswordBox.Password;
            PasswordBox.Visibility     = Visibility.Collapsed;
            PasswordTextBox.Visibility = Visibility.Visible;
            ((System.Windows.Controls.TextBlock)ShowPwdButton.Content).Text = "\uE7B4"; // hide eye
            ShowPwdButton.ToolTip = "Hide password";
            PasswordTextBox.Focus();
            PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
        }
        else
        {
            PasswordBox.Password = PasswordTextBox.Text;
            PasswordTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility     = Visibility.Visible;
            ((System.Windows.Controls.TextBlock)ShowPwdButton.Content).Text = "\uE7B3"; // show eye
            ShowPwdButton.ToolTip = "Show password";
            PasswordBox.Focus();
        }
    }

    private string GetCurrentPassword() =>
        _showPassword ? PasswordTextBox.Text : PasswordBox.Password;

    private void TryAccept()
    {
        if (string.IsNullOrEmpty(GetCurrentPassword()))
        {
            ErrorLabel.Text       = "Please enter a password.";
            ErrorLabel.Foreground = new SolidColorBrush(Color.FromRgb(229, 57, 53));
            ErrorLabel.Visibility = Visibility.Visible;
            return;
        }

        Password     = GetCurrentPassword();
        DialogResult = true;
        Close();
    }

    private static Strength EstimateStrength(string pwd)
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
            Strength.Weak   => new SolidColorBrush(Color.FromRgb(229, 57, 53)),   // red
            Strength.Fair   => new SolidColorBrush(Color.FromRgb(245, 166, 35)),  // amber
            Strength.Strong => new SolidColorBrush(Color.FromRgb(67, 160, 71)),   // green
            _               => new SolidColorBrush(Color.FromRgb(58, 58, 58))
        };
    }
}
