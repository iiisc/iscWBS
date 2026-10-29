using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using iscWBS.Core.Models;
using iscWBS.Helpers;

namespace iscWBS.Converters;

public sealed partial class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => new SolidColorBrush(StatusPalette.Color(value is WbsStatus status ? status : WbsStatus.NotStarted));

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
