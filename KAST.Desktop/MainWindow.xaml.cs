using KAST.Desktop.ViewModels;

using System.Windows;

namespace KAST.Desktop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowsVM();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowsVM).DbContext.SaveChanges();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            categoryDataGrid.Items.Refresh();
            (DataContext as MainWindowsVM).AddMod(modID.Text);
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            foreach (var item in categoryDataGrid.Items)
            {
                if(!(item is Core.Models.Mod mod))
                    continue;

                mod.UpdateModInfos();
            }
        }
    }
}
