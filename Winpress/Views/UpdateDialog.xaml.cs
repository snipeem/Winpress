using System.Windows;
using Winpress.ViewModels;

namespace Winpress.Views;

public partial class UpdateDialog : Window
{
    public UpdateDialog(UpdateViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.CloseRequested += Close;
    }
}
