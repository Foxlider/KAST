using KAST.Core.Managers;
using KAST.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Timers;

namespace KAST.Desktop.ViewModels
{
    internal class MainWindowsVM : KastVM
    {
        public MainWindowsVM()
        {
            Timer myTimer = new();
            myTimer.Elapsed += new ElapsedEventHandler(DisplayTimeEvent);
            myTimer.Interval = 1000; // 1000 ms is one second
            myTimer.Start();

            Mods = new ModManager().GetObservableMods();
        }

        private string _title = "KAST";

        public string KastTitle
        {
            get { return _title; }
            set { _title = value; NotifyPropertyChanged(); }
        }

        private ObservableCollection<Mod> mods;

        public ObservableCollection<Mod> Mods
        {
            get { return mods; }
            set { mods = value; NotifyPropertyChanged(); }
        }

        public void AddMod(string id)
        {
            if (!ulong.TryParse(id, out ulong modID))
                return;

            new ModManager().AddMod(new Mod(modID) { Name = "a", Url = "b" });
        }

        public void DisplayTimeEvent(object source, ElapsedEventArgs e)
        { KastTitle = Utilities.NameGenerator(); }
    }
}
