namespace Adamantium.UI.Controls.Navigation;

/// <summary>A view model that restores its own state from the pane id when a saved layout is loaded; without it a restored
/// pane is a blank instance of its type.</summary>
public interface IRestorablePane
{
    /// <summary>Called on a freshly made view model, before it is put into the region, with the id its pane had.</summary>
    void RestoreFrom(string paneId);
}
