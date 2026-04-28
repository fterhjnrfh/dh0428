using System;
using System.Windows.Data;

namespace Example_Demo
{
    public class MonitorTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (bool?)value == true ? "运行中" : "未运行";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (bool?)value == true ? false : true;
        }
    }
}