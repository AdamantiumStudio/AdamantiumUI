using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.Navigation;

/// <summary>The docking side of a region: one per region, there before a docking control shows it and after that control
/// is gone. The control's adapter attaches an <see cref="IDockingHost"/>; the answers are read from it as asked, and the
/// events are what changed between two of its reports.</summary>
public sealed class DockingRegion : IDockingRegion
{
    private static readonly ConditionalWeakTable<IRegion, DockingRegion> ByRegion = new();

    private IDockingHost _host;

    // What the host held at its last report, by view model: the events are the difference to the next one. Kept across a
    // host leaving, so the control that replaces it reports only what really changed.
    private Dictionary<object, DockedPane> _known = new(ReferenceEqualityComparer.Instance);
    private object _active;
    private readonly List<object> _recent = [];

    // Raised in order even when a handler changes the docking: its changes queue behind the rest of this report.
    private readonly Queue<Action> _pending = new();
    private bool _raising;

    private DockingRegion(IRegion region) => Region = region;

    /// <summary>The docking side of <paramref name="region"/>.</summary>
    public static DockingRegion Of(IRegion region) => ByRegion.GetValue(region, static r => new DockingRegion(r));

    public IRegion Region { get; }

    /// <summary>Connects the docking control that shows the region, in place of the one before.</summary>
    public void Attach(IDockingHost host)
    {
        if (host == null || ReferenceEquals(_host, host)) return;

        if (_host != null) _host.Changed -= OnHostChanged;
        _host = host;
        _host.Changed += OnHostChanged;
        Refresh();
    }

    /// <summary>Disconnects that control, if it is still the one connected.</summary>
    public void Detach(IDockingHost host)
    {
        if (host == null || !ReferenceEquals(_host, host)) return;

        _host.Changed -= OnHostChanged;
        _host = null;
    }

    private IReadOnlyList<DockedPane> Panes => _host?.Panes ?? [];

    private IEnumerable<DockedPane> Known => Panes.Where(pane => pane.ViewModel != null);

    private IEnumerable<DockedPane> Shown => Known.Where(pane => pane.Placement.State != PaneState.Hidden);

    public IReadOnlyList<object> ViewModels => Known.Select(pane => pane.ViewModel).ToList();

    public IReadOnlyList<object> ViewModelsIn(DockZone zone) =>
        Shown.Where(pane => (pane.Placement.Zone & zone) != 0).Select(pane => pane.ViewModel).ToList();

    public IReadOnlyList<object> Documents =>
        Shown.Where(pane => pane.Kind == PaneKind.Document).Select(pane => pane.ViewModel).ToList();

    public IReadOnlyList<object> Tools =>
        Shown.Where(pane => pane.Kind == PaneKind.Tool).Select(pane => pane.ViewModel).ToList();

    public IReadOnlyList<object> Floating => ViewModelsIn(DockZone.Floating);

    public IReadOnlyList<object> Hidden =>
        Known.Where(pane => pane.Placement.State == PaneState.Hidden).Select(pane => pane.ViewModel).ToList();

    /// <summary>The navigation parameter <see cref="OpenDocumentAsync{T}"/> passes the key in.</summary>
    public const string KeyParameter = "key";

    public IReadOnlyList<object> Unsaved => ViewModels.Where(viewModel => viewModel is IDocument { IsDirty: true }).ToList();

    public async Task<bool> SaveAllAsync(CancellationToken cancellationToken = default)
    {
        var saved = true;
        foreach (var document in Unsaved.OfType<IDocument>())
        {
            if (!await document.SaveAsync(cancellationToken)) saved = false;
        }

        return saved;
    }

    public async Task<T> OpenDocumentAsync<T>(string key, NavigationParameters parameters = null,
        CancellationToken cancellationToken = default) where T : class
    {
        if (Find<T>(open => open is IDockablePane pane && pane.PaneId == key) is { } existing)
        {
            Activate(existing);
            return existing;
        }

        var result = await Region.NavigateToAsync<T>((parameters ?? new NavigationParameters()).Add(KeyParameter, key),
            cancellationToken);
        return result.Success ? result.ViewModel as T : null;
    }

    public IReadOnlyList<DockedGroup> Groups => _host?.Groups ?? [];

    public IReadOnlyList<DockedGroup> DocumentGroups => Groups.Where(group => group.Kind == PaneKind.Document).ToList();

    public IReadOnlyList<DockedGroup> ToolGroups => Groups.Where(group => group.Kind == PaneKind.Tool).ToList();

    public DockedGroup GroupOf(object viewModel) => viewModel == null
        ? null
        : Groups.FirstOrDefault(group => group.Panes.Any(pane => ReferenceEquals(pane.ViewModel, viewModel)));

    public IReadOnlyList<object> RecentDocuments
    {
        get
        {
            var documents = Documents;
            var recent = _recent.Where(document => documents.Contains(document, ReferenceEqualityComparer.Instance)).ToList();
            recent.AddRange(documents.Where(document => !recent.Contains(document, ReferenceEqualityComparer.Instance)));
            return recent;
        }
    }

    public object ActivePane => Known.FirstOrDefault(pane => pane.Placement.IsActive)?.ViewModel;

    public object ActiveDocument
    {
        get
        {
            var active = Known.FirstOrDefault(pane => pane.Placement.IsActive);
            return active is { Kind: PaneKind.Document } ? active.ViewModel : RecentDocuments.FirstOrDefault();
        }
    }

    public bool Contains<T>(Func<T, bool> predicate = null) => All<T>().Any(item => predicate == null || predicate(item));

    public T Find<T>(Func<T, bool> predicate = null) where T : class =>
        All<T>().FirstOrDefault(item => predicate == null || predicate(item));

    public IReadOnlyList<T> All<T>() => ViewModels.OfType<T>().ToList();

    public PanePlacement PlacementOf(object viewModel) => PaneOf(viewModel)?.Placement ?? default;

    public bool Activate(object viewModel) => PaneOf(viewModel) is { } pane && _host.Activate(pane.Id);

    public async Task<bool> CloseAsync(object viewModel) =>
        PaneOf(viewModel) is { } pane && await _host.CloseAsync([pane.Id]) == 1;

    public Task<int> CloseAllAsync(Func<object, bool> predicate = null)
    {
        if (_host == null) return Task.FromResult(0);

        var ids = Shown.Where(pane => predicate == null || predicate(pane.ViewModel)).Select(pane => pane.Id).ToList();
        return _host.CloseAsync(ids);
    }

    public async Task<T> ShowToolAsync<T>(NavigationParameters parameters = null,
        CancellationToken cancellationToken = default) where T : class
    {
        if (Find<T>() is { } open)
        {
            Activate(open);
            return open;
        }

        var result = await Region.NavigateToAsync<T>(parameters, cancellationToken);
        return result.Success ? result.ViewModel as T : null;
    }

    public async Task<T> OpenBesideAsync<T>(object target, DockZone side, NavigationParameters parameters = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var result = await Region.NavigateToAsync<T>(parameters, cancellationToken);
        if (!result.Success || result.ViewModel is not T opened) return null;

        if (PaneOf(opened) is { } pane && PaneOf(target) is { } beside) _host.DockBeside(pane.Id, beside.Id, side);
        return opened;
    }

    public event EventHandler<DockingPaneEventArgs> PaneOpened;
    public event EventHandler<DockingPaneEventArgs> PaneClosed;
    public event EventHandler<DockingPaneEventArgs> PlacementChanged;
    public event EventHandler ActivePaneChanged;

    private DockedPane PaneOf(object viewModel) =>
        viewModel == null ? null : Known.FirstOrDefault(pane => ReferenceEquals(pane.ViewModel, viewModel));

    private void OnHostChanged(object sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var now = new Dictionary<object, DockedPane>(ReferenceEqualityComparer.Instance);
        foreach (var pane in Known) now.TryAdd(pane.ViewModel, pane);

        var before = _known;
        _known = now;

        // The panes left behind hear it first: deactivated, then hidden.
        foreach (var (viewModel, was) in before)
        {
            if (was.Placement.IsActive && !(now.TryGetValue(viewModel, out var pane) && pane.Placement.IsActive))
                Tell(viewModel, aware => aware.OnDeactivated());
        }

        foreach (var (viewModel, was) in before)
        {
            if (was.Placement.IsShown && !(now.TryGetValue(viewModel, out var pane) && pane.Placement.IsShown))
                Tell(viewModel, aware => aware.OnHidden());
        }

        // Then what went, what came and what moved.
        foreach (var (viewModel, was) in before)
        {
            if (!now.ContainsKey(viewModel) && was.Placement.State != PaneState.Hidden)
                Raise(Change.Closed, viewModel, was.Placement);
        }

        foreach (var (viewModel, pane) in now)
        {
            var hidden = pane.Placement.State == PaneState.Hidden;
            if (!before.TryGetValue(viewModel, out var was))
            {
                if (!hidden) Raise(Change.Opened, viewModel, pane.Placement);
                continue;
            }

            var wasHidden = was.Placement.State == PaneState.Hidden;
            if (wasHidden && !hidden) Raise(Change.Opened, viewModel, pane.Placement);
            else if (!wasHidden && hidden) Raise(Change.Closed, viewModel, pane.Placement);
            else if (pane.Placement.IsMovedFrom(was.Placement))
            {
                var placement = pane.Placement;
                Tell(viewModel, aware => aware.OnPlacementChanged(placement));
                Raise(Change.Moved, viewModel, placement);
            }
        }

        // The pane arrived at last: shown, then activated.
        foreach (var (viewModel, pane) in now)
        {
            if (pane.Placement.IsShown && !(before.TryGetValue(viewModel, out var was) && was.Placement.IsShown))
                Tell(viewModel, aware => aware.OnShown());
        }

        // The documents worked in, most recent first; a closed one leaves the list.
        var active = now.Values.FirstOrDefault(pane => pane.Placement.IsActive);
        if (active is { Kind: PaneKind.Document })
        {
            _recent.Remove(active.ViewModel);
            _recent.Insert(0, active.ViewModel);
        }
        _recent.RemoveAll(document => !now.ContainsKey(document));

        if (!ReferenceEquals(active?.ViewModel, _active))
        {
            _active = active?.ViewModel;
            if (active != null) Tell(active.ViewModel, aware => aware.OnActivated());
            _pending.Enqueue(() => ActivePaneChanged?.Invoke(this, EventArgs.Empty));
        }

        Drain();
    }

    private void Tell(object viewModel, Action<IDockingAware> call)
    {
        if (viewModel is IDockingAware aware) _pending.Enqueue(() => call(aware));
    }

    private enum Change { Opened, Closed, Moved }

    private void Raise(Change change, object viewModel, PanePlacement placement)
    {
        var args = new DockingPaneEventArgs(viewModel, placement);

        // The handler is read when raised, not now: a handler of an earlier event may subscribe.
        _pending.Enqueue(() => (change switch
        {
            Change.Opened => PaneOpened,
            Change.Closed => PaneClosed,
            _ => PlacementChanged
        })?.Invoke(this, args));
    }

    private void Drain()
    {
        if (_raising) return;

        _raising = true;
        try
        {
            while (_pending.Count > 0) _pending.Dequeue()();
        }
        finally
        {
            _raising = false;
        }
    }
}
