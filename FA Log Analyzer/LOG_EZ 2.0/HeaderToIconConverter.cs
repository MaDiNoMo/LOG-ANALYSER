//using System;
//using System.Windows.Controls;

//namespace LOG_EZ
//{
//    public class HeaderToIconConverter : System.Windows.Data.IValueConverter
//    {
//        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
//        {
//            string sequenceIcon = "M12,16L19.36,10.27L21,9L12,2L3,9L4.63,10.27M12,18.54L4.62,12.81L3,14.07L12,21L21,14.07L19.37,12.8L12,18.54Z";
//            string folderIcon = "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z";
//            string dotIcon = "M12,10A2,2 0 1,0 12,14A2,2 0 1,0 12,10Z";

//            if (value is StackPanel) return System.Windows.Media.Geometry.Parse(dotIcon);

//            string header = value?.ToString() ?? string.Empty;
//            if (header.StartsWith("Sequence", StringComparison.OrdinalIgnoreCase))
//                return System.Windows.Media.Geometry.Parse(sequenceIcon);

//            if (header.Contains("Stream Functions") || header.Contains("S6") || header.Contains("[List]") || header.Contains("[L]") || header.StartsWith("<"))
//                return System.Windows.Media.Geometry.Parse(folderIcon);

//            return System.Windows.Media.Geometry.Parse(dotIcon);
//        }

//        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
//    }
//}
using System;
using System.Windows.Data;

namespace LOG_EZ
{
    public class HeaderToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => null;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }
}