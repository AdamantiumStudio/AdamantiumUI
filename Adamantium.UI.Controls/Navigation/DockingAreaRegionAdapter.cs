using System.Collections.Generic;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Controls.Navigation;

/// <summary>Region on a <see cref="DockingArea"/>: a navigated view model becomes a <see cref="Pane"/> in the document well
/// (or where <see cref="IDockablePane"/> says); an already open pane is activated where it is.</summary>
public sealed class DockingAreaRegionAdapter : IRegionAdapter
{
    private readonly IViewLocator _viewLocator;

    public DockingAreaRegionAdapter(IViewLocator viewLocator)
    {
        _viewLocator = viewLocator;
    }

    public void Attach(IRegion region, IUIComponent host)
    {
        if (host is not DockingArea area) return;

        // Tabs accumulate: opening a second document does not close the first. That is the whole difference between a
        // docking area and a ContentControl, and stating it here keeps the region from removing what it did not open.
        region.SingleActiveView = false;

        var panesByViewModel = new Dictionary<object, string>();
        var syncing = false;

        // What a view model asks the region about its panes is answered from this area while it shows the region.
        var dockingHost = new DockingAreaHost(area, panesByViewModel);

        void Sync()
        {
            if (syncing) return;
            syncing = true;

            var wanted = new HashSet<object>(region.ActiveViewModels);

            // Gone from the region -> gone from the layout.
            List<object> removed = null;
            foreach (var pair in panesByViewModel)
            {
                if (wanted.Contains(pair.Key)) continue;

                (removed ??= []).Add(pair.Key);
            }

            if (removed != null)
            {
                foreach (var viewModel in removed)
                {
                    area.RemovePane(panesByViewModel[viewModel]);
                    panesByViewModel.Remove(viewModel);
                }
            }

            // New in the region -> a pane in the document well.
            var opened = false;
            List<object> replaced = null;
            foreach (var viewModel in region.ActiveViewModels)
            {
                if (panesByViewModel.ContainsKey(viewModel)) continue;

                var pane = PaneFor(viewModel);

                // The id of a closed tool, taken by a new view model: the one kept for it is let go.
                if (area.HiddenPanes.Contains(pane.Id))
                {
                    foreach (var pair in panesByViewModel)
                    {
                        if (pair.Value == pane.Id) (replaced ??= []).Add(pair.Key);
                    }

                    foreach (var kept in replaced ?? []) panesByViewModel.Remove(kept);
                }

                // Paired BEFORE it opens: opening reports the layout, and the report has to know whose pane it is.
                panesByViewModel[viewModel] = pane.Id;
                area.AddPane(pane, (viewModel as IDockablePane)?.PaneZone ?? DockZone.Center);
                opened = true;
            }

            foreach (var kept in replaced ?? []) region.Remove(kept);

            // Only a sync that opened nothing activates the current view: after an open, CurrentViewModel is still the
            // previous view and activating it would switch tabs in an untouched panel.
            if (!opened
                && region.CurrentViewModel != null
                && panesByViewModel.TryGetValue(region.CurrentViewModel, out var current))
            {
                area.Activate(current);
            }

            syncing = false;
        }

        // Named handlers so they can be removed: a view rebuilt on re-entry hands over a new area, and the old one must
        // stop syncing.
        void OnActiveViewsChanged(object s, EventArgs e) => Sync();

        void OnRegionPropertyChanged(object s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IRegion.CurrentViewModel) || syncing) return;
            if (region.CurrentViewModel == null || !panesByViewModel.TryGetValue(region.CurrentViewModel, out var id)) return;

            syncing = true;
            area.Activate(id);
            syncing = false;
        }

        // A navigation to the view model the region already has as current changes nothing the handler above hears, yet
        // it still means "show me this" - a closed tool included.
        void OnNavigated(object s, RegionNavigationEventArgs e)
        {
            if (syncing || !e.Result.Success || e.Result.ViewModel is not { } viewModel) return;
            if (!panesByViewModel.TryGetValue(viewModel, out var id)) return;

            syncing = true;
            area.Activate(id);
            syncing = false;
        }

        // A saved layout names panes this region opened, and at start-up none of them exist yet. The key written with
        // them is the view model's TYPE, so the region can make the very same thing again, put it back in itself, and
        // hand the area the pane - which the layout then finds by id like any other.
        void OnPaneRestoring(object s, PaneRestoringEventArgs e)
        {
            if (e.Pane != null || string.IsNullOrEmpty(e.RestoreKey)) return;

            var viewModel = Recreate(e.RestoreKey, e.PaneId);
            if (viewModel == null) return;

            syncing = true;                       // the pane is being made for a layout, not opened into one
            region.Add(viewModel);
            syncing = false;

            var pane = PaneFor(viewModel);
            pane.Id = e.PaneId;
            panesByViewModel[viewModel] = e.PaneId;
            e.Pane = pane;
        }

        // Closing a document is the user saying that view is done with, so the region must forget it too. Otherwise the
        // region still holds the view model, the next navigation to it REUSES that instance, sees it already "open"
        // and opens nothing at all - a name that can never be reached again once it has been closed. A closed TOOL is
        // put away and keeps its view model: navigating to it brings the same one back.
        void OnPaneClosed(object s, PaneClosedEventArgs e)
        {
            if (e.CanRestore) return;

            foreach (var pair in panesByViewModel)
            {
                if (pair.Value != e.PaneId) continue;

                panesByViewModel.Remove(pair.Key);
                syncing = true;                 // the pane is already out of the layout; Sync must not take it out twice
                region.Remove(pair.Key);
                syncing = false;

                // A closed document is done with for good, unlike a tool that is only put away.
                (pair.Key as IDisposable)?.Dispose();
                break;
            }
        }

        // ...and the other way: the user clicking a tab IS navigation, so the region has to hear about it or the two
        // answers - what is on screen and what the journal thinks - drift apart.
        void OnActivePaneChanged(object s, EventArgs e)
        {
            if (syncing) return;

            var active = ActiveViewModel(area, panesByViewModel);
            if (active == null || ReferenceEquals(active, region.CurrentViewModel)) return;

            syncing = true;
            region.Activate(active);
            syncing = false;
        }

        region.ActiveViewsChanged += OnActiveViewsChanged;
        region.PropertyChanged += OnRegionPropertyChanged;
        region.Navigated += OnNavigated;
        area.PaneRestoring += OnPaneRestoring;
        area.PaneClosed += OnPaneClosed;
        area.ActivePaneChanged += OnActivePaneChanged;

        // ...and taken off again the moment this area is gone for good. Without it the adapter outlives its control: the
        // region goes on calling into an area nobody can see, which keeps its own layout, its own panes and its own idea
        // of what is open - and every document opened afterwards is added to that one as well as to the live one.
        void Release(ReadOnlySpan<IFundamentalUIComponent> gone)
        {
            // The batch is everything discarded together - our area is at most one of them, so this only asks whether it
            // is in there at all.
            var ours = false;
            foreach (var component in gone)
            {
                if (!ReferenceEquals(component, area)) continue;
                ours = true;
                break;
            }
            if (!ours) return;

            region.ActiveViewsChanged -= OnActiveViewsChanged;
            region.PropertyChanged -= OnRegionPropertyChanged;
            region.Navigated -= OnNavigated;
            area.PaneRestoring -= OnPaneRestoring;
            area.PaneClosed -= OnPaneClosed;
            area.ActivePaneChanged -= OnActivePaneChanged;
            DockingRegion.Of(region).Detach(dockingHost);
            dockingHost.Release();
            DiscardedVisuals.Discarded -= Release;
        }

        DiscardedVisuals.Discarded += Release;

        Sync();
        DockingRegion.Of(region).Attach(dockingHost);
    }

    /// <summary>The view model behind the pane the user is looking at, or null when it is a pane the region never
    /// opened (an authored tool panel, say).</summary>
    private static object ActiveViewModel(DockingArea area, Dictionary<object, string> panes)
    {
        foreach (var pane in area.Panes)
        {
            if (!pane.IsSelected) continue;

            foreach (var pair in panes)
            {
                if (pair.Value == pane.Id) return pair.Key;
            }
        }

        return null;
    }

    /// <summary>Wraps a view model in a pane. The BODY is resolved by the view locator - the pane holds the view model
    /// itself and lets the template selector turn it into a view, which is what keeps the region free of UI types.</summary>
    // Makes a view model again from what was saved with its pane. The key is the type; anything the instance itself
    // knew (which page it was showing, say) it restores from its own id - see IRestorablePane.
    private static object Recreate(string restoreKey, string paneId)
    {
        var type = Type.GetType(restoreKey, throwOnError: false);
        if (type == null) return null;

        var context = UIAppContext.Current?.UIContext;
        var viewModel = context != null ? context.Resolve(type) : Activator.CreateInstance(type);

        (viewModel as IRestorablePane)?.RestoreFrom(paneId);
        return viewModel;
    }

    private Pane PaneFor(object viewModel)
    {
        var placement = viewModel as IDockablePane;

        var pane = new Pane
        {
            Id = placement?.PaneId ?? viewModel.GetType().Name,
            Allowed = placement?.PaneAllowed ?? DockZone.All,
            Kind = placement?.PaneKind ?? PaneKind.Document,
            MinSize = placement?.PaneMinSize ?? 0,

            // What it takes to make this one again if a saved layout is loaded when it does not exist: its TYPE. The
            // instance restores the rest of itself from its own id.
            RestoreKey = viewModel.GetType().AssemblyQualifiedName,
            Content = viewModel,
            ContentTemplateSelector = new ViewLocatorTemplateSelector(_viewLocator)
        };

        if (placement == null)
        {
            pane.Header = viewModel;
            return pane;
        }

        // Bound, not copied: the tab follows the title, and the place goes both ways - a view model with a setter moves
        // its pane, and a drag tells it where the pane went.
        pane.SetBinding(TabItem.HeaderProperty, new Binding(nameof(IDockablePane.PaneTitle))
        {
            Source = viewModel,
            Mode = BindingMode.OneWay,
            TargetNullValue = viewModel
        });
        pane.SetBinding(Pane.ZoneProperty,
            new Binding(nameof(IDockablePane.PaneZone)) { Source = viewModel, Mode = BindingMode.TwoWay });
        return pane;
    }
}
