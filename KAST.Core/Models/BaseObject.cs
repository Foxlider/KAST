using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KAST.Core.Models
{
    public class BaseObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            // ReSharper disable once ConstantConditionalAccessQualifier
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
