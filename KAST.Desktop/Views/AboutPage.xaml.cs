using KAST.Desktop.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace KAST.Desktop.Views;

public sealed partial class AboutPage
{
    public AboutViewModel ViewModel
    {
        get;
    }

    public AboutPage()
    {
        ViewModel = App.GetService<AboutViewModel>();
        InitializeComponent();
    }
}
