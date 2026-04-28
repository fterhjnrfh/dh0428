using System;
using System.Windows.Data;
using System.Windows.Media;

namespace Example_Demo
{
    public class IOColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is int)
            {
                switch ((int)value)
                {
                    case 0:
                        return Brushes.Green;
                    case 1:
                        return Brushes.Red;
                    default:
                        break;
                }
            }
            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}