using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Winpress.ViewModels;

namespace Winpress.Views;

public partial class ProgressWindow : Window
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public ProgressWindow(ExtractionProgressViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // Auto-close when cancelled so the user does not have to click anything
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ExtractionProgressViewModel.IsCancelled))
                Dispatcher.Invoke(() => { if (vm.IsCancelled) Close(); });
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        int trueValue = 1;
        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, sizeof(int));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
