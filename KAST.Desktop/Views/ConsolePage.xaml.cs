using KAST.Desktop.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace KAST.Desktop.Views;

public sealed partial class ConsolePage
{
    //Code for presenter formatting
    //https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Controls/SampleCodePresenter.xaml.cs

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
