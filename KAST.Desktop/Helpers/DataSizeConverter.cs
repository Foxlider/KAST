using Microsoft.UI.Xaml.Data;
using System;
using System.Globalization;

namespace KAST.Desktop.Helpers
{
    class DataSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string culture)
        {
            if (value is long size)
            {
                double fullSize = size;
                string[] sizes = { " B", "KB", "MB", "GB", "TB" };
                var order = 0;
                while (fullSize >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    fullSize /= 1024.0;
                }

                // Adjust the format string to your preferences. For example "{0:0.#}{1}" would
                // show a single decimal place, and no space.
                return $"{fullSize,7:F} {sizes[order],-2}";
            }
            else
            {
                Console.WriteLine("WAT");
            }

            return $"{0,7:F} B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string culture)
        { return null; }
    }
}
