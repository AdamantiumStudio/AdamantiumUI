using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.DataGrid;

/// <summary>The strip above the table that searches it: a field, how many cells hold what was typed, and the two steps
/// through them.
/// <para>The search RUNS on Enter and on the button, not on every keystroke: one pass reads every shown column of
/// every row, which is not something to spend per letter - measured at some 380 ms over ten thousand rows.</para></summary>
public class DataGridSearchPanel : Control
{
    /// <summary>Where the search stands. The words for it are the theme's, made of this, <see cref="CurrentMatch"/> and
    /// <see cref="MatchCount"/>.</summary>
    public static readonly AdamantiumProperty StateProperty = AdamantiumProperty.Register(nameof(State),
        typeof(DataGridSearchState), typeof(DataGridSearchPanel), new PropertyMetadata(DataGridSearchState.Idle));

    /// <summary>Which of the cells found is the current one, counted from one.</summary>
    public static readonly AdamantiumProperty CurrentMatchProperty = AdamantiumProperty.Register(nameof(CurrentMatch),
        typeof(int), typeof(DataGridSearchPanel), new PropertyMetadata(0));

    /// <summary>How many cells hold what was typed.</summary>
    public static readonly AdamantiumProperty MatchCountProperty = AdamantiumProperty.Register(nameof(MatchCount),
        typeof(int), typeof(DataGridSearchPanel), new PropertyMetadata(0));

    private TreeDataGrid _owner;
    private TextBox _text;
    private ButtonBase _find;
    private ButtonBase _next;
    private ButtonBase _previous;
    private ButtonBase _close;

    public DataGridSearchState State
    {
        get => GetValue<DataGridSearchState>(StateProperty);
        private set => SetValue(StateProperty, value);
    }

    public int CurrentMatch
    {
        get => GetValue<int>(CurrentMatchProperty);
        private set => SetValue(CurrentMatchProperty, value);
    }

    public int MatchCount
    {
        get => GetValue<int>(MatchCountProperty);
        private set => SetValue(MatchCountProperty, value);
    }

    /// <summary>The grid this strip searches. Setting it REGISTERS the strip with that grid, which is what lets a new
    /// count reach it.</summary>
    public TreeDataGrid Owner
    {
        get => _owner;
        internal set
        {
            if (ReferenceEquals(_owner, value)) return;
            _owner = value;
            _owner?.AdoptSearchPanel(this);
            Sync();
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _text = GetTemplateChild("PART_Text") as TextBox;
        _find = GetTemplateChild("PART_Find") as ButtonBase;
        _next = GetTemplateChild("PART_Next") as ButtonBase;
        _previous = GetTemplateChild("PART_Previous") as ButtonBase;
        _close = GetTemplateChild("PART_Close") as ButtonBase;

        // ENTER PRESSED, not KeyDown: a single-line TextBox handles its own keys and raises this instead, so a KeyDown
        // handler here saw everything EXCEPT the one key the strip cares about.
        if (_text != null) _text.EnterPressed += OnEnterPressed;
        if (_find != null) _find.Click += OnFind;
        if (_next != null) _next.Click += OnNext;
        if (_previous != null) _previous.Click += OnPrevious;
        if (_close != null) _close.Click += OnClose;

        Sync();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();

        if (_text != null) _text.EnterPressed -= OnEnterPressed;
        if (_find != null) _find.Click -= OnFind;
        if (_next != null) _next.Click -= OnNext;
        if (_previous != null) _previous.Click -= OnPrevious;
        if (_close != null) _close.Click -= OnClose;

        _text = null;
        _find = null;
        _next = null;
        _previous = null;
        _close = null;
    }

    /// <summary>Brings the count in line with what the grid found.</summary>
    internal void Sync()
    {
        var total = Owner?.MatchCount ?? 0;
        var walking = Owner?.IsSearching == true;

        MatchCount = total;
        CurrentMatch = Owner?.CurrentMatch ?? 0;

        // Nothing typed says nothing: an empty field with "0 of 0" under it reads as a failed search rather than as
        // a search that was never made.
        // A walk in progress says so: the count grows as the table is read, and a number that keeps changing with no
        // word beside it reads as a table that cannot make up its mind.
        State = string.IsNullOrEmpty(Owner?.SearchText) ? DataGridSearchState.Idle
            : total == 0 ? (walking ? DataGridSearchState.Searching : DataGridSearchState.NoMatches)
            : walking ? DataGridSearchState.MatchesSoFar : DataGridSearchState.Matches;
    }

    // Enter SEARCHES, and searches again from where it stands - the same key that starts a find is the one that walks
    // it, which is what every editor does.
    private void OnEnterPressed(object sender, KeyEventArgs e)
    {
        if (Owner == null) return;

        e.Handled = true;
        if (string.Equals(Owner.SearchText, _text?.Text)) Owner.FindNext();
        else Find();
    }

    private void OnFind(object sender, RoutedEventArgs e) => Find();

    private void OnNext(object sender, RoutedEventArgs e) => Owner?.FindNext();

    private void OnPrevious(object sender, RoutedEventArgs e) => Owner?.FindPrevious();

    // Closes the strip from inside and calls the search off. SetCurrentValue, so the binding that opened it can open it again.
    private void OnClose(object sender, RoutedEventArgs e)
    {
        Owner?.SetCurrentValue(TreeDataGrid.ShowSearchPanelProperty, false);
    }

    private void Find()
    {
        if (Owner == null) return;

        Owner.SetCurrentValue(TreeDataGrid.SearchTextProperty, _text?.Text);
        Owner.Search();
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        TemplatedParent == null ? new GroupAutomationPeer(this) : null;
}
