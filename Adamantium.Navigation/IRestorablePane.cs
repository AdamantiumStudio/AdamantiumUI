namespace Adamantium.Navigation;

/// <summary>A view model that restores its own state from the pane id when a saved layout is loaded; without it a restored
/// pane is a blank instance of its type.</summary>
public interface IRestorablePane
{
    /// <summary>Called on a freshly made view model, before it is put into the region, with the id its pane had.</summary>
    void RestoreFrom(string paneId);

    /// <summary>What it wants back with the layout - what it scrolled to, which of its tabs was open - as text, or null.
    /// Asked whenever the layout is saved.</summary>
    string SaveState() => null;

    /// <summary>Given back what <see cref="SaveState"/> said, once the layout is loaded.</summary>
    void RestoreState(string state) { }
}
