using KAST.Core.Models;
using KAST.Desktop.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace KAST.Desktop.Views;

// TODO: Change the grid as appropriate for your app. Adjust the column definitions on DataGridPage.xaml.
// For more details, see the documentation at https://docs.microsoft.com/windows/communitytoolkit/controls/datagrid.
public sealed partial class ModsPage
{
    public ModsViewModel ViewModel
    {
        get;
    }

    public ModsPage()
    {
        ViewModel = App.GetService<ModsViewModel>();
        InitializeComponent();
    }

    private void Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button b || b.DataContext is not Mod m)
            return;

        m.IsLoading = !m.IsLoading;


        var console = App.GetService<ConsoleViewModel>();
        console.ConsoleOutput = console.ConsoleOutput + "Test\n";
    }
}
