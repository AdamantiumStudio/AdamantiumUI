using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Controls.Navigation;

/// <summary>A <see cref="DockingArea"/> as the host of a <see cref="DockingRegion"/>: its panes reported with their view
/// models - the region's own, and for a pane written in markup, its content's. A view model that is an
/// <see cref="IDocument"/> marks its tab and is saved when the area is told to save it.</summary>
internal sealed class DockingAreaHost : IDockingHost
{
    private readonly DockingArea _area;
    private readonly Dictionary<object, string> _panesByViewModel;

    public DockingAreaHost(DockingArea area, Dictionary<object, string> panesByViewModel)
    {
        _area = area;
        _panesByViewModel = panesByViewModel;
        _area.LayoutChanged += OnLayoutChanged;
        _area.ActivePaneChanged += OnLayoutChanged;   // a tab clicked: another pane at the front of its panel
        _area.PanesSaving += OnPanesSaving;
        _area.PaneStateSaving += OnPaneStateSaving;
        _area.PaneStateRestoring += OnPaneStateRestoring;
    }

    public void Release()
    {
        _area.LayoutChanged -= OnLayoutChanged;
        _area.ActivePaneChanged -= OnLayoutChanged;
        _area.PanesSaving -= OnPanesSaving;
        _area.PaneStateSaving -= OnPaneStateSaving;
        _area.PaneStateRestoring -= OnPaneStateRestoring;
    }

    private void OnPaneStateSaving(object sender, PaneStateEventArgs e)
    {
        if (_area.PaneById(e.PaneId) is { } pane && ViewModelOf(pane) is IRestorablePane restorable)
            e.State = restorable.SaveState();
    }

    private void OnPaneStateRestoring(object sender, PaneStateEventArgs e)
    {
        if (_area.PaneById(e.PaneId) is { } pane && ViewModelOf(pane) is IRestorablePane restorable)
            restorable.RestoreState(e.State);
    }

    public IReadOnlyList<DockedPane> Panes
    {
        get
        {
            var active = _area.ActivePaneId;
            var panes = new List<DockedPane>();

            foreach (var pane in _area.Panes)
            {
                var group = _area.Layout.FindGroup(pane.Id);
                var state = group.State == PaneGroupState.Docked ? PaneState.Open : PaneState.Collapsed;

                // A folded panel is seen only while its body is revealed, and then only its front tab.
                var front = group.ActiveIndex >= 0 && group.ActiveIndex < group.PaneIds.Count
                            && group.PaneIds[group.ActiveIndex] == pane.Id;
                var shown = front && group.State != PaneGroupState.Collapsed;

                panes.Add(Report(pane, new PanePlacement(pane.Zone, state, pane.Id == active, shown)));
            }

            foreach (var id in _area.HiddenPanes)
            {
                if (_area.PaneById(id) is { } pane)
                    panes.Add(Report(pane, new PanePlacement(pane.Zone, PaneState.Hidden, false, false)));
            }

            return panes;
        }
    }

    public IReadOnlyList<DockedGroup> Groups
    {
        get
        {
            var panes = new Dictionary<string, DockedPane>();
            foreach (var pane in Panes) panes[pane.Id] = pane;

            var layout = _area.Layout;
            var groups = new List<DockedGroup>();
            foreach (var node in layout.Groups)
            {
                var members = new List<DockedPane>();
                foreach (var id in node.PaneIds)
                {
                    if (panes.TryGetValue(id, out var pane)) members.Add(pane);
                }

                var front = node.ActiveIndex >= 0 && node.ActiveIndex < node.PaneIds.Count
                    ? members.Find(pane => pane.Id == node.PaneIds[node.ActiveIndex])
                    : null;

                groups.Add(new DockedGroup(members,
                    layout.IsDocument(node) ? PaneKind.Document : PaneKind.Tool,
                    layout.ZoneOf(node),
                    node.State == PaneGroupState.Docked ? PaneState.Open : PaneState.Collapsed,
                    front));
            }

            return groups;
        }
    }

    private DockedPane Report(Pane pane, PanePlacement placement) =>
        new(pane.Id, ViewModelOf(pane), pane.Kind, placement);

    private object ViewModelOf(Pane pane)
    {
        foreach (var pair in _panesByViewModel)
        {
            if (pair.Value == pane.Id) return pair.Key;
        }

        // Written in markup: the view model its content has of its own, not the one it inherits from the area.
        return pane.Content is IFundamentalUIComponent content && !ReferenceEquals(content.DataContext, pane.DataContext)
            ? content.DataContext
            : null;
    }

    public bool Activate(string paneId) => _area.Activate(paneId);

    public Task<int> CloseAsync(IReadOnlyList<string> paneIds) => _area.ClosePanesAsync(paneIds);

    public bool DockBeside(string paneId, string targetPaneId, DockZone side) =>
        _area.DockBeside(paneId, targetPaneId, side);

    public event EventHandler Changed;

    private void OnLayoutChanged(object sender, EventArgs e)
    {
        MarkDocuments();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Each tab of an IDocument follows its IsDirty - bound once, the first time the pane is seen with it.
    private void MarkDocuments()
    {
        foreach (var pane in _area.Panes)
        {
            if (ViewModelOf(pane) is not IDocument document) continue;
            if (BindingEngine.GetBindingExpression(pane, Pane.IsDirtyProperty) != null) continue;

            pane.SetBinding(Pane.IsDirtyProperty,
                new Binding(nameof(IDocument.IsDirty)) { Source = document, Mode = BindingMode.OneWay });
        }
    }

    private async Task OnPanesSaving(object sender, PanesSavingEventArgs e)
    {
        foreach (var id in e.PaneIds)
        {
            if (_area.PaneById(id) is not { } pane || ViewModelOf(pane) is not IDocument document) continue;
            if (!await document.SaveAsync()) e.Failed = true;
        }
    }
}
