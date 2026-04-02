using Microsoft.UI.Xaml.Data;
using iscWBS.Core.Models;

namespace iscWBS.Converters;

public sealed class StatusToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        WbsStatus? status = value switch
        {
            WbsStatus s => s,
            int i       => (WbsStatus)i,
            _           => null
        };
        return status switch
        {
            WbsStatus.NotStarted => "Not Started",
            WbsStatus.InProgress => "In Progress",
            WbsStatus.Complete   => "Complete",
            WbsStatus.Blocked    => "Blocked",
            _                    => string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
