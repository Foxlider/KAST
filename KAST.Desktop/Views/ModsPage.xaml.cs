using KAST.Desktop.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace KAST.Desktop.Views;

// TODO: Change the grid as appropriate for your app. Adjust the column definitions on DataGridPage.xaml.
// For more details, see the documentation at https://docs.microsoft.com/windows/communitytoolkit/controls/datagrid.
public sealed partial class ModsPage : Page
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
}
