using KAST.Desktop.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace KAST.Desktop.Views;

public sealed partial class ConsolePage
{
    public ConsoleViewModel ViewModel
    {
        get;
    }

    public ConsolePage()
    {
        ViewModel = App.GetService<ConsoleViewModel>();
        InitializeComponent();
    }
}
