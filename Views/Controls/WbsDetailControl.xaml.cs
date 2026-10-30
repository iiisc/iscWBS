using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iscWBS.Core.Models;
using iscWBS.ViewModels;

namespace iscWBS.Views.Controls;

public sealed partial class WbsDetailControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(WbsTreeViewModel),
            typeof(WbsDetailControl),
            new PropertyMetadata(null, (d, _) =>
            {
                if (d is WbsDetailControl c) c.Bindings.Update();
            }));

    public WbsTreeViewModel? ViewModel
    {
        get => (WbsTreeViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public WbsDetailControl()
    {
        InitializeComponent();
    }

    public static string NoteDate(DateTime createdAt) => createdAt.ToString("d MMM yyyy, HH:mm");

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NodeNote note })
            ViewModel?.DeleteNoteCommand.Execute(note);
    }

    private void RemoveDependency_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DependencyRowViewModel row })
            ViewModel?.RemoveDependencyCommand.Execute(row);
    }
}
