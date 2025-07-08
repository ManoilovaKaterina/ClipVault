using System;
using System.Globalization;
using System.Windows.Data;

namespace ClipboardManager.Utils
{
    public class PinButtonTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPinned)
            {
                return isPinned ? "Unpin" : "Pin";
            }
            return "Pin";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}