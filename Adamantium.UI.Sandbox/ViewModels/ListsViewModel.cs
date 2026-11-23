using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Lists tab: one collection in single- and multi-select ListBoxes with two-way selection and live Add/Remove,
/// plus 20 000 rows behind a filtering, paging CollectionView.</summary>
[ViewModel]
public partial class ListsViewModel : TabPageViewModel
{
    public ListsViewModel() : base("Lists")
    {
        Large = new CollectionView(BigItems) { PageSize = 25 };
        Large.SortDescriptions.Add(new SortDescription(nameof(BigItem.Name)));

        PageSteps.CollectionChanged += (_, _) => PageStepsText = string.Join("   ", PageSteps);
        PageStepsText = string.Join("   ", PageSteps);

        Server = new AsyncPagedSource(FetchServerPage, 25);
        Server.PropertyChanged += OnServerChanged;
        _ = Server.MoveToPageAsync(0);   // nobody fetches the first page for you: a source that pages is asked, not read
    }

    public ObservableCollection<string> Items { get; } = new()
    {
        "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune"
    };

    [Bindable] private string _selectedItem = "Earth";

    private int _counter;

    [Command] private void Add() => Items.Add($"New planet {++_counter}");

    [Command] private void RemoveSelected()
    {
        if (SelectedItem != null) Items.Remove(SelectedItem);
    }

    // --- Tiles in groups ------------------------------------------------------------------------------------------

    private readonly Adamantium.Core.Collections.TrackingCollection<Body> _bodies =
    [
        new("Sun", "Stars"),
        new("Mercury", "Planets"), new("Venus", "Planets"), new("Earth", "Planets"), new("Mars", "Planets"),
        new("Jupiter", "Planets"), new("Saturn", "Planets"), new("Uranus", "Planets"), new("Neptune", "Planets"),
        new("Moon", "Moons"), new("Phobos", "Moons"), new("Deimos", "Moons"), new("Io", "Moons"), new("Europa", "Moons"),
    ];

    private CollectionView _bodiesView;

    /// <summary>The bodies of a solar system by kind: one list of tiles, each kind's header a line of its own.</summary>
    public CollectionView Bodies => _bodiesView ??= GroupedBodies();

    private CollectionView GroupedBodies()
    {
        var view = new CollectionView(_bodies);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Body.Kind)));
        return view;
    }

    /// <summary>A body dragged to where it was dropped: before the body it landed before, or at its group's end - and
    /// into that group, of the kind it now is.</summary>
    [Command]
    private void DropBody(object arg)
    {
        if (arg is not DragDropEventArgs { DropGroup.Name: string kind } e || e.Data?.Get<Body>() is not { } body)
        {
            return;
        }

        _bodies.Remove(body);
        body.Kind = kind;
        var at = e.InsertBefore is Body before ? _bodies.IndexOf(before) : -1;
        if (at < 0)
        {
            // The group's end: after its last body, or - the body was its only one - at the end.
            var last = LastIndexOfKind(kind);
            at = last >= 0 ? last + 1 : _bodies.Count;
        }

        _bodies.Insert(at, body);
    }

    private int LastIndexOfKind(string kind)
    {
        for (var i = _bodies.Count - 1; i >= 0; i--)
        {
            if (_bodies[i].Kind == kind)
            {
                return i;
            }
        }

        return -1;
    }

    // --- The large list ------------------------------------------------------------------------------------------

    /// <summary>Twenty thousand rows - enough that showing them all is a real decision rather than a formality.</summary>
    public ObservableCollection<BigItem> BigItems { get; } =
        new(Enumerable.Range(1, 20000).Select(i => new BigItem($"Item {i:D5}", i)));

    /// <summary>The view the list and the pager BOTH look at: filtered, sorted, and cut into pages, in that order.</summary>
    public CollectionView Large { get; }

    /// <summary>The search box. Every keystroke re-filters, which is the point of the stand.</summary>
    [Bindable] private string _search = string.Empty;

    // ── A SERVER THAT HANDS OUT ONE PAGE AT A TIME ─────────────────────────────────────────────────────────────────
    // A simulated network source with switchable delay and failure: unknown totals, superseded requests, failed fetches.

    /// <summary>How long the fake server takes to answer, in milliseconds. Long enough to SEE the in-flight state.</summary>
    [Bindable] private int _serverDelay = 600;

    /// <summary>Armed by the button; the next request fails and disarms it. One failure, deliberately: a source that
    /// always failed would only show an empty list, and the point is what survives ONE.</summary>
    private bool _failNextRequest;

    /// <summary>What the fake server holds. Deliberately NOT a round number of pages - the last one comes back short,
    /// which is how the end is discovered rather than announced.</summary>
    private const int ServerRowCount = 237;

    /// <summary>The paged source the second list and its pager both look at.</summary>
    public AsyncPagedSource Server { get; }

    /// <summary>What the server is doing - the only place the reader can see a request that is in flight, a total that is
    /// not known yet, or a page that failed to arrive - with the page, the rows known so far and the failure.</summary>
    [Bindable] private ServerState _serverState = ServerState.Loading;

    [Bindable] private int _serverPage = 1;

    [Bindable] private int? _serverTotal;

    [Bindable] private string _serverError;

    [Command]
    private void FailNextServerRequest()
    {
        _failNextRequest = true;
        ServerState = ServerState.Armed;
    }

    [Command]
    private void ReloadServerPage() => _ = Server.MoveToPageAsync(Server.PageIndex);

    private async Task<PageResult> FetchServerPage(PageRequest request, CancellationToken cancellation)
    {
        await Task.Delay(Math.Max(0, ServerDelay), cancellation);

        if (_failNextRequest)
        {
            _failNextRequest = false;
            throw new InvalidOperationException($"the server refused page {request.PageIndex + 1}");
        }

        var from = request.PageIndex * request.PageSize;
        var count = Math.Max(0, Math.Min(request.PageSize, ServerRowCount - from));
        var rows = Enumerable.Range(from, count)
            .Select(object (i) => $"Row {i + 1:0000} — served")
            .ToList();

        // The total is NOT reported. The pager has to discover the end by walking into a short page, which is the
        // honest state for a query whose count nobody has run.
        return new PageResult(rows);
    }

    private void OnServerChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ServerPage = Server.PageIndex + 1;
        ServerTotal = Server.TotalItemCount;
        ServerError = Server.LastError?.Message;
        ServerState = Server.IsPageChanging
            ? ServerState.Loading
            : Server.LastError != null ? ServerState.Failed : ServerState.Shown;
    }

    /// <summary>The page sizes this stand offers - NOT the control's own 10/25/50/100, on purpose: the progression
    /// belongs to the application, and a stand that took the default would never show that.
    /// <para>Observable, and edited in place by the two commands below, because "does it update live" is a question
    /// about the COLLECTION and not about the property - handing over a new list would answer a different one.</para></summary>
    public ObservableCollection<int> PageSteps { get; } = [15, 30, 60, 120];

    /// <summary>The progression as one line of text. A LABEL, not a list: an items host lays its rows out on a uniform
    /// pitch, which is right for rows of equal size and wrong for numbers of two, three and four digits - the narrow
    /// ones got a gap and the wide ones ran together. A readout has no need of a host.</summary>
    [Bindable] private string _pageStepsText = string.Empty;

    [Command]
    private void AddStep()
    {
        var next = PageSteps.Count > 0 ? PageSteps[^1] * 2 : 15;
        if (!PageSteps.Contains(next)) PageSteps.Add(next);
    }

    [Command]
    private void DropCurrentStep() => PageSteps.Remove(Large.PageSize);

    // THE FLAGS ENUM AS FIVE SWITCHES. DisplayMode is one value carrying five independent bits, and a check box is the
    // control for a bit - so the stand offers the bits and assembles the value, which is also the only way to try a
    // combination without editing markup and rebuilding.
    [Bindable] private bool _showFirstLast = true;
    [Bindable] private bool _showPreviousNext = true;
    [Bindable] private bool _showNumeric = true;
    [Bindable] private bool _showPageSizeSelector = true;
    [Bindable] private bool _showPageInput = true;

    /// <summary>What the switches add up to - the value both pagers are given.</summary>
    [Bindable] private PagerDisplayMode _pagerMode = PagerDisplayMode.All;

    partial void OnShowFirstLastChanged(bool value) => RebuildPagerMode();

    partial void OnShowPreviousNextChanged(bool value) => RebuildPagerMode();

    partial void OnShowNumericChanged(bool value) => RebuildPagerMode();

    partial void OnShowPageSizeSelectorChanged(bool value) => RebuildPagerMode();

    partial void OnShowPageInputChanged(bool value) => RebuildPagerMode();

    private void RebuildPagerMode()
    {
        var mode = PagerDisplayMode.None;
        if (ShowFirstLast) mode |= PagerDisplayMode.FirstLast;
        if (ShowPreviousNext) mode |= PagerDisplayMode.PreviousNext;
        if (ShowNumeric) mode |= PagerDisplayMode.Numeric;
        if (ShowPageSizeSelector) mode |= PagerDisplayMode.PageSizeSelector;
        if (ShowPageInput) mode |= PagerDisplayMode.PageInput;
        PagerMode = mode;
    }

    partial void OnSearchChanged(string value)
    {
        var text = value?.Trim();
        Large.Filter = string.IsNullOrEmpty(text)
            ? null
            : o => ((BigItem)o).Name.Contains(text, System.StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>What the simulated server of the lists page is doing.</summary>
public enum ServerState
{
    Loading,
    Armed,
    Failed,
    Shown,
}

/// <summary>A row of the large list: something to show and something to sort by.</summary>
public sealed class BigItem(string name, int number)
{
    /// <summary>What the row shows.</summary>
    public string Name { get; } = name;

    /// <summary>Its position in the original set, so a sort has something of its own to order by.</summary>
    public int Number { get; } = number;

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>A tile of the grouped list: a body of a solar system and the kind it is grouped by.</summary>
public sealed class Body(string name, string kind)
{
    public string Name { get; } = name;

    /// <summary>Stars, planets or moons: the group it is shown in; a drop into another group changes it.</summary>
    public string Kind { get; set; } = kind;

    /// <inheritdoc/>
    public override string ToString() => Name;
}
