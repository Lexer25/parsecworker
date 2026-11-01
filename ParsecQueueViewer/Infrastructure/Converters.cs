using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ParsecQueueViewer.Infrastructure
{
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool visible = !(value is bool boolean && boolean);
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Цвета статусов PARSEC_QUEUE. Параметр: Background или Foreground.
    /// </summary>
    public sealed class StatusBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush BlueBackground = Freeze("#DBEAFE");
        private static readonly SolidColorBrush BlueForeground = Freeze("#1D4ED8");
        private static readonly SolidColorBrush AmberBackground = Freeze("#FEF3C7");
        private static readonly SolidColorBrush AmberForeground = Freeze("#B45309");
        private static readonly SolidColorBrush GreenBackground = Freeze("#DCFCE7");
        private static readonly SolidColorBrush GreenForeground = Freeze("#15803D");
        private static readonly SolidColorBrush RedBackground = Freeze("#FEE2E2");
        private static readonly SolidColorBrush RedForeground = Freeze("#B91C1C");
        private static readonly SolidColorBrush GrayBackground = Freeze("#EEF2F7");
        private static readonly SolidColorBrush GrayForeground = Freeze("#475569");
        private static readonly SolidColorBrush SlateBackground = Freeze("#E2E8F0");
        private static readonly SolidColorBrush SlateForeground = Freeze("#334155");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int status = value is int number ? number : 0;
            bool background = string.Equals(parameter as string, "Background", StringComparison.OrdinalIgnoreCase);

            switch (status)
            {
                case 0:
                    return background ? BlueBackground : BlueForeground;
                case 1:
                    return background ? AmberBackground : AmberForeground;
                case 2:
                    return background ? GreenBackground : GreenForeground;
                case 3:
                    return background ? AmberBackground : AmberForeground;
                case 4:
                    return background ? RedBackground : RedForeground;
                case 5:
                    return background ? GrayBackground : GrayForeground;
                case 6:
                    return background ? SlateBackground : SlateForeground;
                default:
                    return background ? GrayBackground : GrayForeground;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        private static SolidColorBrush Freeze(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }

}
