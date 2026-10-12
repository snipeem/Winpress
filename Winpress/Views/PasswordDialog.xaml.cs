using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
// Explicit alias — prevents ambiguity with System.Windows.Forms when UseWindowsForms=true
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Winpress.Views;

public partial class PasswordDialog : Window
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public string? Password { get; private set; }

    public PasswordDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
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

    private void TryAccept()
    {
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            ErrorLabel.Text       = "Please enter a password.";
            ErrorLabel.Visibility = Visibility.Visible;
            return;
        }

        Password = PasswordBox.Password;
        DialogResult = true;
        Close();
    }
}
