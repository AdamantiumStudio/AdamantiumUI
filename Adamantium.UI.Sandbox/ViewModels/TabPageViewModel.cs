namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Base for a gallery tab's view-model. Carries the strip <see cref="Header"/> the TabControl's ItemTemplate
/// binds; each concrete type is mapped to its View for the body by <see cref="TabViewSelector"/>. Composing one of these
/// per tab under <see cref="GalleryViewModel"/> is the "real app" shape - a single root view-model owning child ones.</summary>
public abstract class TabPageViewModel
{
    protected TabPageViewModel(string header) => Header = header;

    /// <summary>Which page this is, for the tab strip: the key of its title among the gallery's phrases.</summary>
    public string Header { get; }
}
