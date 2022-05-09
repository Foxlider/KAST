using KAST.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Timers;
using System.Windows.Data;

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

            DbContext.Database.EnsureCreated();
            DbContext.Mods.Load();
            Mods = new();
            Mods = DbContext.Mods.Local.ToObservableCollection();
        }

        private string _title = "KAST";

        public string KastTitle
        {
            get { return _title; }
            set { _title = value; NotifyPropertyChanged(); }
        }

        //public CollectionViewSource Mods { get; set; } = new();

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

            DbContext.Mods.Add(new Mod {ModID=modID, Name = "a", Url = "b" });
            DbContext.SaveChanges();
        }

        public void DisplayTimeEvent(object source, ElapsedEventArgs e)
        { KastTitle = Utilities.NameGenerator(); }
    }
}
