using System.Timers;

namespace KAST.Desktop.ViewModels
{
    internal class MainWindowsVM : KastVM
    {
        public MainWindowsVM()
        {
            Timer myTimer = new Timer();
            myTimer.Elapsed += new ElapsedEventHandler(DisplayTimeEvent);
            myTimer.Interval = 1000; // 1000 ms is one second
            myTimer.Start();
        }

        private string _title = "KAST";

        public string KastTitle
        {
            get { return _title; }
            set { _title = value; NotifyPropertyChanged(); }
        }


        public void DisplayTimeEvent(object source, ElapsedEventArgs e)
        { KastTitle = Core.Tools.Utilities.NameGenerator(); }
    }
}
