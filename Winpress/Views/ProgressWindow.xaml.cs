using System.Windows;
using Winpress.ViewModels;

namespace Winpress.Views;

public partial class ProgressWindow : Window
{
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
