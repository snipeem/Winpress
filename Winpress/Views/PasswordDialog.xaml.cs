using System.Windows;
using System.Windows.Input;
// Explicit alias — prevents ambiguity with System.Windows.Forms when UseWindowsForms=true
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Winpress.Views;

public partial class PasswordDialog : Window
{
    public string? Password { get; private set; }

    public PasswordDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
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
