using Microsoft.UI.Xaml.Controls;
using iscWBS.ViewModels;

namespace iscWBS.Views.Controls;

/// <summary>Undo and redo buttons for a page's toolbar; Ctrl+Z and Ctrl+Y work anywhere in the window.</summary>
public sealed partial class UndoRedoButtons : UserControl
{
    public UndoViewModel Undo { get; } = App.Services.Undo;

    public UndoRedoButtons() => InitializeComponent();
}
