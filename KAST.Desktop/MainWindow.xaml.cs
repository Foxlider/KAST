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
    }
}
