using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.Navigation;

/// <summary>A docking region as a view model sees it: what is open where, and what can be done to a pane besides where it
/// stands. Moving a pane is setting its view model's <see cref="IDockablePane.PaneZone"/> - there is no move here. Got
/// from <see cref="IRegionManager.Docking"/>; until a docking control shows the region, it holds nothing.</summary>
public interface IDockingRegion
{
    /// <summary>The region itself, to navigate it.</summary>
    IRegion Region { get; }

    /// <summary>The view model of every pane, closed tools included, in layout order. A pane written in markup counts
    /// with its content's view model.</summary>
    IReadOnlyList<object> ViewModels { get; }

    /// <summary>The view models of the panes in a zone, or in any of several (<see cref="DockZone.Edges"/>). Closed tools
    /// are in none.</summary>
    IReadOnlyList<object> ViewModelsIn(DockZone zone);

    IReadOnlyList<object> Documents { get; }

    /// <summary>The tools in the layout; closed ones are <see cref="Hidden"/>.</summary>
    IReadOnlyList<object> Tools { get; }

    IReadOnlyList<object> Floating { get; }

    /// <summary>The tools closed and kept, to be brought back with <see cref="Activate"/>.</summary>
    IReadOnlyList<object> Hidden { get; }

    /// <summary>Every panel - the document area's groups and the tools' - in layout order, as descriptions.</summary>
    IReadOnlyList<DockedGroup> Groups { get; }

    /// <summary>The groups of the document area: one until it is split.</summary>
    IReadOnlyList<DockedGroup> DocumentGroups { get; }

    IReadOnlyList<DockedGroup> ToolGroups { get; }

    /// <summary>The panel holding that view model's pane, or null - a closed tool is in none.</summary>
    DockedGroup GroupOf(object viewModel);

    /// <summary>The view models with work not saved yet (<see cref="IDocument.IsDirty"/>).</summary>
    IReadOnlyList<object> Unsaved { get; }

    /// <summary>Saves every <see cref="Unsaved"/> one, asking nobody; false when one of them could not be saved.</summary>
    Task<bool> SaveAllAsync(CancellationToken cancellationToken = default);

    /// <summary>The document for <paramref name="key"/> - a file path, say - brought to the front, or opened by navigation
    /// with the key among the parameters (<see cref="DockingRegion.KeyParameter"/>). The key is its pane's id, so the same
    /// thing is never open twice. Null when the navigation did not happen.</summary>
    Task<T> OpenDocumentAsync<T>(string key, NavigationParameters parameters = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>The documents, the one worked in most recently first.</summary>
    IReadOnlyList<object> RecentDocuments { get; }

    /// <summary>The pane being worked in, or null.</summary>
    object ActivePane { get; }

    /// <summary>The document worked in last - still that one while a tool is being worked in.</summary>
    object ActiveDocument { get; }

    bool Contains<T>(Func<T, bool> predicate = null);

    /// <summary>The first view model of that type that matches, or null.</summary>
    T Find<T>(Func<T, bool> predicate = null) where T : class;

    IReadOnlyList<T> All<T>();

    /// <summary>Where that view model's pane is; the default placement when it has none here.</summary>
    PanePlacement PlacementOf(object viewModel);

    /// <summary>Brings its pane to the front - back into the layout if it is a closed tool.</summary>
    bool Activate(object viewModel);

    /// <summary>Closes its pane the way the close button does: the application is asked first.</summary>
    Task<bool> CloseAsync(object viewModel);

    /// <summary>Closes every pane whose view model matches, or every pane; returns how many closed. An answer that cancels
    /// all stops it.</summary>
    Task<int> CloseAllAsync(Func<object, bool> predicate = null);

    /// <summary>The pane of that type brought to the front - opened by navigation when there is none, so there is one
    /// however often it is asked for. Null when the navigation did not happen.</summary>
    Task<T> ShowToolAsync<T>(NavigationParameters parameters = null, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Opens a view model by navigation and docks its pane against a side of the panel holding
    /// <paramref name="target"/>'s. Null when the navigation did not happen.</summary>
    Task<T> OpenBesideAsync<T>(object target, DockZone side, NavigationParameters parameters = null,
        CancellationToken cancellationToken = default) where T : class;

    event EventHandler<DockingPaneEventArgs> PaneOpened;

    /// <summary>A pane closed; a tool is then <see cref="Hidden"/>.</summary>
    event EventHandler<DockingPaneEventArgs> PaneClosed;

    /// <summary>A pane went to another zone, or folded away, or came back from its strip.</summary>
    event EventHandler<DockingPaneEventArgs> PlacementChanged;

    event EventHandler ActivePaneChanged;
}
