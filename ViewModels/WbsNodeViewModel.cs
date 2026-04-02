using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using iscWBS.Core.Models;

namespace iscWBS.ViewModels;

public sealed partial class WbsNodeViewModel : ObservableObject
{
    public static readonly WbsNodeViewModel Placeholder = new();

    public WbsNode Node { get; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    public bool IsNotEditing => !IsEditing;

    public ObservableCollection<WbsNodeViewModel> Children { get; } = new();

    private WbsNodeViewModel()
    {
        Node = new WbsNode();
    }

    public WbsNodeViewModel(WbsNode node, bool hasChildren)
    {
        Node = node;
        Title = node.Title;
        if (hasChildren)
            Children.Add(Placeholder);
    }

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(IsNotEditing));
}
