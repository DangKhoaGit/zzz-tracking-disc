using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ZZZBuffTracker.App;

public partial class OverlayContent : UserControl
{
    public OverlayContent() => InitializeComponent();
}

public sealed class SpacingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(0, 0, value is double spacing ? spacing : 0, value is double gap ? gap : 0);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
