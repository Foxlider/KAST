using Microsoft.UI.Xaml.Data;
using System;

namespace KAST.Desktop.Helpers
{
    class ModIDConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if(value == null || value is not ulong id)
                return null;

            if (id < ulong.MaxValue - ushort.MaxValue)
                return id.ToString();
            return id - ulong.MaxValue + ushort.MaxValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        { throw new NotImplementedException(); }
    }
}
