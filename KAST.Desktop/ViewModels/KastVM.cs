using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KAST.Desktop.ViewModels
{
    internal partial class KastVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged == null)
                return;
            PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
