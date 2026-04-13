using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using iscWBS.Core.Models;

namespace iscWBS.Converters;

/// <summary>Maps a <see cref="WbsStatus"/> value to a <see cref="SolidColorBrush"/> matching the status colour.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        WbsStatus? status = value switch
        {
            WbsStatus s => s,
            int i       => (WbsStatus)i,
            _           => null
        };

        Windows.UI.Color color = status switch
        {
            WbsStatus.InProgress => Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4),
            WbsStatus.Complete   => Windows.UI.Color.FromArgb(0xFF, 0x10, 0x7C, 0x10),
            WbsStatus.Blocked    => Windows.UI.Color.FromArgb(0xFF, 0xC5, 0x0F, 0x1F),
            _                    => Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80)
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
