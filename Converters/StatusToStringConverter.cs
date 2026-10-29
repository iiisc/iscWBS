using Microsoft.UI.Xaml.Data;
using iscWBS.Core.Models;
using iscWBS.Helpers;

namespace iscWBS.Converters;

public sealed partial class StatusToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is WbsStatus status ? status.ToDisplayString() : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
