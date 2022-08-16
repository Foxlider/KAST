using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace KAST.Desktop.Helpers
{
    class BoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null || value is not bool vis)
                return value;
            vis = ((string)parameter ==  "Reverse" ? !vis : vis);
            return vis ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value == null || value is not Visibility vis)
                return null;

            return vis == Visibility.Visible;
        }
    }
}
