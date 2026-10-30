namespace iscWBS.Views;

/// <summary>A page showing project data, which undo and redo bring up to date.</summary>
public interface IReloadablePage
{
    /// <summary>Saves an edit the page is still holding, such as text not yet committed.</summary>
    Task FlushAsync() => Task.CompletedTask;

    void Reload();
}
