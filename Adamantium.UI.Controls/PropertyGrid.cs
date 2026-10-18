using System.Collections;
using System.Collections.Specialized;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>What was written, and to what. A null <see cref="Property"/> means every property of that object.</summary>
public class PropertyValuesChangedEventArgs : EventArgs
{
    public PropertyValuesChangedEventArgs(object target = null, PropertyDefinition property = null,
        IReadOnlyList<object> targets = null)
    {
        Target = target;
        Property = property;
        Targets = targets ?? (target == null ? Array.Empty<object>() : new[] { target });
    }

    public object Target { get; }

    public PropertyDefinition Property { get; }

    /// <summary>EVERY object the write goes to. One line of an inspector can be pointed at a whole selection, and
    /// something recording the change - undo - has to know about all of them, not only the first.</summary>
    public IReadOnlyList<object> Targets { get; }
}

/// <summary>An inspector: names and values split by a draggable grip, in folding sections. Declared as
/// <see cref="PropertyDefinition"/>s, drawn as <see cref="PropertyRow"/>s; one definition serves every selected object.</summary>
public class PropertyGrid : Control
{
    public static readonly AdamantiumProperty SelectedObjectProperty = AdamantiumProperty.Register(
        nameof(SelectedObject), typeof(object), typeof(PropertyGrid),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, OnSelectedObjectChanged));

    /// <summary>The objects being inspected together. An editor almost always has several things selected, and one
    /// definition then stands for the same property on all of them: it shows their common value, says so when they
    /// differ, and writes to every one of them.</summary>
    public static readonly AdamantiumProperty SelectedObjectsProperty = AdamantiumProperty.Register(
        nameof(SelectedObjects), typeof(IEnumerable), typeof(PropertyGrid),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, OnSelectedObjectChanged));

    /// <summary>Width of the NAME half. One number for the whole inspector - a per-section width would let the columns
    /// of two sections drift apart, and the thing stops reading as a table.</summary>
    public static readonly AdamantiumProperty NameColumnWidthProperty = AdamantiumProperty.Register(
        nameof(NameColumnWidth), typeof(Double), typeof(PropertyGrid),
        new PropertyMetadata(140.0, PropertyMetadataOptions.AffectsMeasure, OnNameColumnWidthChanged));

    public static readonly AdamantiumProperty MinNameColumnWidthProperty = AdamantiumProperty.Register(
        nameof(MinNameColumnWidth), typeof(Double), typeof(PropertyGrid), new PropertyMetadata(60.0));

    public static readonly AdamantiumProperty MinValueColumnWidthProperty = AdamantiumProperty.Register(
        nameof(MinValueColumnWidth), typeof(Double), typeof(PropertyGrid), new PropertyMetadata(60.0));

    public static readonly AdamantiumProperty IndentProperty = AdamantiumProperty.Register(nameof(Indent),
        typeof(Double), typeof(PropertyGrid), new PropertyMetadata(14.0, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>How tall every editor in the inspector stands. ONE number, set by the theme: left to themselves a field
    /// is as tall as a line of text, a spinner as tall as its buttons, and a list collapses when nobody filled it - and
    /// the form reads as a pile. A check box is the exception and keeps its square.</summary>
    public static readonly AdamantiumProperty EditorHeightProperty = AdamantiumProperty.Register(nameof(EditorHeight),
        typeof(Double), typeof(PropertyGrid), new PropertyMetadata(20.0, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>What an editor says while the selected objects hold more than one value between them, shown as the
    /// prompt of an empty editor. Only then: a row they all agree on shows that value like any other row.
    /// <para>One phrase rather than the values themselves - a hundred objects selected is the ordinary case, and
    /// listing what each holds would fill the row with a line nobody can read. The theme says it in the application's
    /// language.</para></summary>
    public static readonly AdamantiumProperty MixedTextProperty = AdamantiumProperty.Register(nameof(MixedText),
        typeof(String), typeof(PropertyGrid), new PropertyMetadata(null, OnMixedTextChanged));

    /// <summary>When lines show their reset button; Always by default (dimmed when nothing to reset), so lines do not
    /// change shape.</summary>
    public static readonly AdamantiumProperty ResetButtonProperty = AdamantiumProperty.Register(nameof(ResetButton),
        typeof(ResetButtonState), typeof(PropertyGrid),
        new PropertyMetadata(ResetButtonState.Always, OnResetButtonChanged));

    /// <summary>The sections or the objects to inspect. When <see cref="Sections"/> holds sections with a
    /// <see cref="PropertySection.DataType"/>, each of them is pointed at the items of its type - several edited together
    /// - and shown in its own
    /// place, beside the sections written for no type; the items no section is written for are shown only with
    /// <see cref="AutoGenerateSections"/>, and sections in the source are not shown at all. Otherwise this
    /// replaces <see cref="Sections"/>: an item that is not a section is an object to inspect, and the grid builds its
    /// own section for it. Changed on another thread - a game world's - the grid follows on its own.</summary>
    public static readonly AdamantiumProperty SectionsSourceProperty = AdamantiumProperty.Register(
        nameof(SectionsSource), typeof(IEnumerable), typeof(PropertyGrid),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, OnSectionsSourceChanged));

    /// <summary>Builds the sections from the type of the selected objects instead of <see cref="Sections"/>. Each grid
    /// builds its own, so two views of one view-model never fight over the same sections.</summary>
    public static readonly AdamantiumProperty AutoGenerateSectionsProperty = AdamantiumProperty.Register(
        nameof(AutoGenerateSections), typeof(Boolean), typeof(PropertyGrid),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsMeasure, OnSelectedObjectChanged));

    /// <summary>Shows only properties whose name contains this, ignoring case; a matching section keeps all its lines.
    /// Folded parts holding matches open while searching.</summary>
    public static readonly AdamantiumProperty SearchTextProperty = AdamantiumProperty.Register(nameof(SearchText),
        typeof(String), typeof(PropertyGrid), new PropertyMetadata(null, OnSearchTextChanged));

    /// <summary>Whether the inspector carries its search field. An inspector of five rows does not need one.
    /// <para>Taking the field away drops whatever was being searched for: an inspector left narrowed down with nothing
    /// on it to widen it again is a trap, and looks like an inspector that has lost most of its properties.</para>
    /// </summary>
    public static readonly AdamantiumProperty ShowSearchProperty = AdamantiumProperty.Register(nameof(ShowSearch),
        typeof(Boolean), typeof(PropertyGrid),
        new PropertyMetadata(true, PropertyMetadataOptions.AffectsMeasure, OnShowSearchChanged));

    private readonly List<PropertyRow> _rows = new();
    private readonly List<PropertySection> _openedBySearch = new();
    private readonly Dictionary<PropertySection, BindingExpressionBase> _foldingHeldBySearch = new();
    private readonly List<PropertyDefinition> _watched = new();
    private readonly List<INotifyCollectionChanged> _followed = new();
    private readonly Dictionary<object, PropertySection> _built = new(ReferenceEqualityComparer.Instance);
    private readonly List<PropertySection> _dropped = [];
    private readonly List<object> _sourceItems = [];
    private readonly HashSet<object> _claimed = new(ReferenceEqualityComparer.Instance);
    private readonly CrossThreadChanges<NotifyCollectionChangedEventArgs> _sourceChanges;

    /// <summary>How many times the whole panel has been torn down and put back - most of what an inspector costs, and
    /// nothing else said it was happening, so a click that rebuilt the panel looked exactly like a click that did
    /// nothing.</summary>
    public static long Rebuilds;
    private bool _rebuilding;
    private bool _rebuildAgain;
    private Panel _host;
    private TextBox _search;
    private PropertyRow _selected;
    private PropertyDefinitionBuilder _builder;
    private IReadOnlyList<PropertySection> _generated = [];
    private Type _generatedFor;

    static PropertyGrid()
    {
        FocusableProperty.OverrideMetadata(typeof(PropertyGrid), new PropertyMetadata(true));
    }

    public PropertyGrid()
    {
        Sections.CollectionChanged += OnSectionsChanged;
        _sourceChanges = new CrossThreadChanges<NotifyCollectionChangedEventArgs>(_ => Rebuild());
    }

    /// <summary>The sections, in display order. [Content], so an inspector is written as the list of sections it is.</summary>
    [Content]
    public PropertySections Sections { get; } = new();

    public IEnumerable SectionsSource
    {
        get => GetValue<IEnumerable>(SectionsSourceProperty);
        set => SetValue(SectionsSourceProperty, value);
    }

    public Boolean AutoGenerateSections
    {
        get => GetValue<Boolean>(AutoGenerateSectionsProperty);
        set => SetValue(AutoGenerateSectionsProperty, value);
    }

    /// <summary>The object being inspected. A section may name its own <see cref="PropertySection.Target"/>; the rest
    /// read this one.</summary>
    public object SelectedObject
    {
        get => GetValue(SelectedObjectProperty);
        set => SetValue(SelectedObjectProperty, value);
    }

    public IEnumerable SelectedObjects
    {
        get => GetValue<IEnumerable>(SelectedObjectsProperty);
        set => SetValue(SelectedObjectsProperty, value);
    }

    /// <summary>Everything a row stands for right now: the several, or the one, or nothing.</summary>
    public IReadOnlyList<object> Targets
    {
        get
        {
            if (SelectedObjects == null)
                return SelectedObject == null ? Array.Empty<object>() : new[] { SelectedObject };

            var many = new List<object>();
            foreach (var item in SelectedObjects)
            {
                if (item != null) many.Add(item);
            }

            return many;
        }
    }

    public Double NameColumnWidth
    {
        get => GetValue<Double>(NameColumnWidthProperty);
        set => SetValue(NameColumnWidthProperty, value);
    }

    public Double MinNameColumnWidth
    {
        get => GetValue<Double>(MinNameColumnWidthProperty);
        set => SetValue(MinNameColumnWidthProperty, value);
    }

    public Double MinValueColumnWidth
    {
        get => GetValue<Double>(MinValueColumnWidthProperty);
        set => SetValue(MinValueColumnWidthProperty, value);
    }

    /// <summary>Pixels of indent per level of nesting, applied to the NAME half only.</summary>
    public Double Indent
    {
        get => GetValue<Double>(IndentProperty);
        set => SetValue(IndentProperty, value);
    }

    public Double EditorHeight
    {
        get => GetValue<Double>(EditorHeightProperty);
        set => SetValue(EditorHeightProperty, value);
    }

    public String MixedText
    {
        get => GetValue<String>(MixedTextProperty);
        set => SetValue(MixedTextProperty, value);
    }

    public ResetButtonState ResetButton
    {
        get => GetValue<ResetButtonState>(ResetButtonProperty);
        set => SetValue(ResetButtonProperty, value);
    }

    public String SearchText
    {
        get => GetValue<String>(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public Boolean ShowSearch
    {
        get => GetValue<Boolean>(ShowSearchProperty);
        set => SetValue(ShowSearchProperty, value);
    }

    /// <summary>Drops the search and shows every property again. What Escape does, wherever the focus is in the
    /// inspector.</summary>
    public void ClearSearch()
    {
        if (String.IsNullOrEmpty(SearchText)) return;

        SearchText = null;
        _search?.Focus();
    }


    /// <summary>The row the pointer or the keyboard is on.</summary>
    public PropertyRow SelectedRow => _selected;

    /// <summary>Raised after a value has been written.</summary>
    public event EventHandler<PropertyValuesChangedEventArgs> ValueChanged;

    /// <summary>Raised BEFORE a value is written - the one moment at which what is about to be replaced can still be
    /// read. Not cancellable: it exists so a change can be REMEMBERED, not refused.</summary>
    public event EventHandler<PropertyValuesChangedEventArgs> ValueChanging;

    /// <summary>Rebuilds every section's rows - after the sections change, the object changes, or a composite folds.</summary>
    public void Rebuild()
    {
        Rebuilds++;

        // One pass at a time: a nested rebuild would add sections twice, and a duplicate child loops the paint order.
        // A request from inside reruns afterwards.
        if (_rebuilding)
        {
            _rebuildAgain = true;
            return;
        }

        _rebuilding = true;
        try
        {
            do
            {
                _rebuildAgain = false;
                RebuildCore();
            }
            while (_rebuildAgain);
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void RebuildCore()
    {
        // BEFORE the guard, and over every definition rather than over the rows: a line that is hidden has no row, so a
        // row could never hear the change that brings it back. This is what makes IsVisible mean anything after the
        // first pass - an inspector whose lines come and go with what is selected asks for exactly that.
        Watch();
        UnfollowItems();

        if (_host == null) return;

        _host.Children.Clear();
        _rows.Clear();

        var wanted = SearchText?.Trim();
        var searching = !String.IsNullOrEmpty(wanted);

        // The sections the search forced open are opened again from scratch every pass, so whatever is still in the
        // list is a section the user had folded himself before this search started - give it back.
        if (!searching) ReleaseOpened();

        var displayed = DisplayedSections().ToList();
        foreach (var section in displayed)
        {
            // A section named for what is being looked for keeps all of its properties: asking for "Transform" means
            // the whole of it, not the one row that happens to repeat the word.
            var whole = searching && Carries(section.Header as String, wanted);

            // Reuse the panel and rows where the shape is unchanged; a row is re-aimed at new targets and keeps its
            // template, the expensive part.
            var rows = section.Content as StackPanel;
            if (rows == null)
            {
                rows = new StackPanel { Orientation = Orientation.Vertical };
                section.Content = rows;
            }

            // A section may inspect something of its own - all the source's objects of its type; the rest follow the
            // selection - all of it.
            var targets = section.Target == null ? Targets
                : section.Targets is { Count: > 1 } several ? several
                : new[] { section.Target };
            var at = 0;

            foreach (var definition in section.Properties)
            {
                AddRow(rows, definition, targets, 0, whole ? null : wanted, ref at);
            }

            // Whatever the last pass left beyond what this one wanted. Taken off the END, so the rows that stayed keep
            // their places and their templates.
            while (rows.Children.Count > at)
            {
                var leaving = rows.Children[^1];
                rows.Children.RemoveAt(rows.Children.Count - 1);
                DiscardedVisuals.PublishSubtree(leaving);
            }

            // A section with nothing in it is not an empty section, it is one the search has no answer from - and a
            // column of empty headers reads as a result.
            if (searching && rows.Children.Count == 0) continue;

            _host.Children.Add(section);

            // SetCurrentValue, not the setter: folding state is two-way bindable, and a Local value written from here
            // would outrank the binding and leave the section deaf to its own view-model ever after. A bound one rests
            // while the search holds the section open, so the object it is bound to keeps its own folding, and reads
            // it again - from whatever the section shows by then - when the search ends.
            if (searching && !section.IsExpanded)
            {
                _openedBySearch.Add(section);
                if (BindingEngine.GetBindingExpression(section, Expander.IsExpandedProperty) is { } folding)
                {
                    folding.CloseConnection();
                    _foldingHeldBySearch[section] = folding;
                }

                section.SetCurrentValue(Expander.IsExpandedProperty, true);
            }
        }

        // A section written for a type with no object, not shown, lets go of its rows: they went on reading the last one.
        foreach (var section in Sections)
        {
            if (section.DataType == null || section.Target != null || displayed.Contains(section)
                || section.Content is not StackPanel idle) continue;

            while (idle.Children.Count > 0)
            {
                var leaving = idle.Children[^1];
                idle.Children.RemoveAt(idle.Children.Count - 1);
                DiscardedVisuals.PublishSubtree(leaving);
            }
        }

        // Refresh every line's bindings now that the sections are in the tree: definitions from resources could not
        // resolve {Ancestor} earlier.
        foreach (var row in _rows)
        {
            if (row.Definition is { } line && BindingEngine.HasBindings(line)) BindingEngine.RefreshBindings(line);
        }

        DiscardDropped();
    }

    /// <summary>Also discards the sections it built that are out of the tree, such as those a search leaves out: the
    /// discard of its tree does not reach them.</summary>
    protected override void OnDiscarded()
    {
        base.OnDiscarded();
        _dropped.AddRange(_built.Values);
        _dropped.AddRange(_generated);
        DiscardDropped();
    }

    private void DiscardDropped()
    {
        foreach (var section in _dropped)
        {
            _openedBySearch.Remove(section);
            _foldingHeldBySearch.Remove(section);
            if (section.VisualParent == null)
            {
                DiscardedVisuals.PublishSubtree(section);
            }
        }

        _dropped.Clear();
    }

    private void ReleaseOpened()
    {
        foreach (var section in _openedBySearch)
        {
            if (_foldingHeldBySearch.Remove(section, out var folding)) folding.EstablishConnection();
            else section.SetCurrentValue(Expander.IsExpandedProperty, false);
        }

        _openedBySearch.Clear();
    }

    private static bool Carries(String text, String wanted) =>
        text != null && text.Contains(wanted, StringComparison.CurrentCultureIgnoreCase);

    // Whether this definition, or anything under it, answers the search. A composite whose CHILD matches has to stand:
    // hiding the parent would hide the answer.
    private static bool Answers(PropertyDefinition definition, String wanted)
    {
        if (Carries(definition.Header as String, wanted)) return true;
        if (definition is not CompositeProperty composite) return false;

        foreach (var child in composite.Children)
        {
            if (Answers(child, wanted)) return true;
        }

        return false;
    }

    /// <summary>What a definition holds on ONE object, read through its binding. For a question asked once - a test, a
    /// report; a row on screen keeps its bindings live instead.</summary>
    public object ValueOf(object target, PropertyDefinition definition) => Read(target, definition?.Binding);

    /// <summary>What a BINDING answers on one object, asked once. The same question <see cref="ValueOf"/> asks, for a
    /// caller holding a binding that belongs to no line of its own - an items list naming each of its items.</summary>
    public object Read(object target, BindingBase binding)
    {
        if (target == null || binding == null) return null;

        var probe = new BoundValue();
        probe.PointAt(target, binding);
        var value = probe.Value;
        probe.Release();

        return value;
    }

    /// <summary>Writes one value to ONE object through a definition's binding - the mirror of <see cref="ValueOf"/>,
    /// and for the same kind of caller: something that knows what it wants written and has no row to write it through.
    /// <para>Undo is that caller. A step that puts a color back cannot go through a ROW, because by then the rows may
    /// be showing something else entirely - or nothing.</para></summary>
    public bool WriteTo(object target, PropertyDefinition definition, object value)
    {
        if (target == null || definition?.Binding == null) return false;

        var probe = new BoundValue();
        probe.PointAt(target, definition.Binding);
        probe.Value = value;
        probe.Release();

        return true;
    }

    /// <summary>What a definition holds across SEVERAL objects: their common value, or nothing at all when they differ -
    /// which <paramref name="mixed"/> is what says. A blank cell with no explanation would read as "empty", and the user
    /// would overwrite four objects thinking he was filling one in.</summary>
    public object ValueOf(IReadOnlyList<object> targets, PropertyDefinition definition, out bool mixed)
    {
        mixed = false;
        if (targets == null || targets.Count == 0 || definition == null) return null;

        var first = ValueOf(targets[0], definition);
        for (var i = 1; i < targets.Count; i++)
        {
            if (Equals(ValueOf(targets[i], definition), first)) continue;

            mixed = true;
            return null;
        }

        return first;
    }

    /// <summary>Writes what a row's editor produced to every object the row stands for. Called by the row when its
    /// editor says the user changed something - there is no edit MODE to enter or leave: the editors are live.</summary>
    public bool Write(PropertyRow row, object edited)
    {
        if (row?.Definition == null || row.IsReadOnly) return false;

        var about = new PropertyValuesChangedEventArgs(row.Targets.Count > 0 ? row.Targets[0] : null,
            row.Definition, row.Targets);

        // BEFORE anything is written, because that is the only moment the previous value still exists. Whoever wants to
        // take the edit back needs it: a color or a width leaves no trace in a comparison of where things are, so the
        // only way to undo one is to have read it while it was still there. The grid says so and keeps nothing - what
        // becomes of it is not an inspector's business.
        ValueChanging?.Invoke(this, about);

        // INTO the value first, where the definition says the value is an object with parts rather than a thing to be
        // replaced. Nothing is written through the binding then: the property still points at the same object, which is
        // the whole point - everything else holding it follows.
        if (row.WriteInto(edited))
        {
            ValueChanged?.Invoke(this, about);
            Refresh(null, row.Definition);
            return true;
        }

        // Every selected object gets the SAME value - that is what editing a multiple selection means.
        if (!row.Definition.TryConvert(edited, row.ValueType, out var value)) return false;
        if (!row.WriteValue(value)) return false;

        ValueChanged?.Invoke(this, about);
        Refresh(null, row.Definition);
        return true;
    }

    /// <summary>Takes back what was written into one line, where the objects themselves know what was written - the
    /// value is dropped rather than replaced, so a style's or a theme's answer comes back. Reported like any other
    /// edit: whoever keeps the undo of a write keeps the undo of taking it back.</summary>
    public bool Reset(PropertyRow row)
    {
        if (row?.Definition == null || row.IsReadOnly) return false;

        var about = new PropertyValuesChangedEventArgs(row.Targets.Count > 0 ? row.Targets[0] : null,
            row.Definition, row.Targets);

        ValueChanging?.Invoke(this, about);

        if (!row.ResetValues()) return false;

        ValueChanged?.Invoke(this, about);
        Refresh(null, row.Definition);
        return true;
    }

    /// <summary>Flips a boolean row. What the box in the row does, offered for a keyboard shortcut or a test.</summary>
    public bool ToggleRow(PropertyRow row)
    {
        if (row?.Definition == null || row.IsReadOnly) return false;

        // Mixed goes to TRUE, not to "the opposite of nothing": with three objects half on and half off, the one thing
        // the user cannot have meant is to leave them disagreeing.
        var current = !row.IsMixed && row.Value as bool? == true;
        return Write(row, !current);
    }

    /// <summary>Opens or folds a composite row, showing or hiding its children.</summary>
    public void ToggleComposite(PropertyRow row)
    {
        if (row?.Definition is not CompositeProperty composite) return;

        composite.IsExpanded = !composite.IsExpanded;
        Rebuild();
    }

    internal void SelectRow(PropertyRow row) => _selected = row;

    /// <summary>Re-reads one property, or every one when told nothing in particular.</summary>
    public void Refresh(object target = null, PropertyDefinition definition = null)
    {
        foreach (var row in _rows)
        {
            if (definition != null && !ReferenceEquals(row.Definition, definition)) continue;
            if (target != null && !row.Targets.Contains(target)) continue;

            row.Attach(this, row.Definition, row.Targets, row.Indent);
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        UnhookSearch();

        _host = GetTemplateChild("PART_Sections") as Panel;
        _search = GetTemplateChild("PART_Search") as TextBox;

        if (_search != null)
        {
            _search.Text = SearchText;
            _search.PropertyChanged += OnSearchTyped;
            _search.KeyDown += OnSearchKeyDown;
        }

        Rebuild();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        UnhookSearch();

        _search = null;
        _host = null;
    }

    private void UnhookSearch()
    {
        if (_search == null) return;

        _search.PropertyChanged -= OnSearchTyped;
        _search.KeyDown -= OnSearchKeyDown;
    }

    private void OnSearchTyped(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty) SearchText = _search?.Text;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || String.IsNullOrEmpty(SearchText)) return;

        ClearSearch();
        e.Handled = true;
    }

    // Escape anywhere in the inspector, not only in the field: a search is narrowed down, then a row is clicked, and by
    // then the field no longer has the focus - and the way out has to be the same key it was a moment ago.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || e.Key != Key.Escape || String.IsNullOrEmpty(SearchText)) return;

        ClearSearch();
        e.Handled = true;
    }

    private static void OnShowSearchChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is PropertyGrid grid && e.NewValue is false) grid.ClearSearch();
    }

    private static void OnSearchTextChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not PropertyGrid grid) return;

        var wanted = e.NewValue as String;
        if (grid._search != null && grid._search.Text != wanted) grid._search.Text = wanted;

        grid.Rebuild();
    }

    /// <summary>The sections actually shown: <see cref="Sections"/>, those from <see cref="SectionsSource"/>, or both
    /// when sections are written for a <see cref="PropertySection.DataType"/>. Asking points those sections at the
    /// items of the source as it stands.</summary>
    public IEnumerable<PropertySection> Displayed => DisplayedSections();

    private IEnumerable<PropertySection> DisplayedSections()
    {
        if (SectionsSource == null)
        {
            if (AutoGenerateSections) return GeneratedSections();

            // A section written for a type has nothing to show without a source to find its object in.
            if (!Sections.Any(s => s.DataType != null)) return Sections;

            foreach (var section in Sections)
            {
                if (section.DataType != null)
                {
                    section.Target = null;
                    section.Targets = null;
                }
            }

            // The designer has no source to give: every section is shown, empty, so the layout can be seen.
            if (Design.IsDesignMode) return Sections;

            return Sections.Where(s => s.DataType == null).ToList();
        }

        ReadSource();
        var many = new List<PropertySection>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var typed = Sections.Any(s => s.DataType != null);
        _claimed.Clear();

        if (typed)
        {
            foreach (var section in Sections)
            {
                if (section.DataType == null)
                {
                    many.Add(section);
                    continue;
                }

                var ofType = Unclaimed(section.DataType);
                section.Targets = ofType;
                section.Target = ofType.Count > 0 ? ofType[0] : null;
                if (section.Target == null)
                {
                    continue;
                }

                foreach (var item in ofType)
                {
                    _claimed.Add(item);
                }
                many.Add(section);
            }
        }

        foreach (var item in _sourceItems)
        {
            if (item is PropertySection section)
            {
                if (!typed) many.Add(section);
            }
            else if (item != null && !_claimed.Contains(item) && (!typed || AutoGenerateSections))
            {
                many.Add(SectionFor(item));
                seen.Add(item);
            }
        }

        foreach (var gone in _built.Keys.Where(item => !seen.Contains(item)).ToList())
        {
            _dropped.Add(_built[gone]);
            _built.Remove(gone);
        }

        return many;
    }

    // Under the source's own lock where it has one: a world adding a component on its own thread is never seen halfway.
    private void ReadSource()
    {
        _sourceItems.Clear();
        var source = SectionsSource;
        object[] copy;
        int count;
        using (var gate = SourceGate.Hold(source))
        {
            copy = gate.Copy(source, out count);
            if (copy == null)
            {
                foreach (var item in source) _sourceItems.Add(item);
                return;
            }
        }

        for (var i = 0; i < count; i++) _sourceItems.Add(copy[i]);
        SourceGate.Return(copy);
    }

    private List<object> Unclaimed(Type type)
    {
        var ofType = new List<object>();
        foreach (var item in _sourceItems)
        {
            if (item != null && !_claimed.Contains(item) && type.IsInstanceOfType(item)) ofType.Add(item);
        }

        return ofType;
    }

    private PropertySection SectionFor(object item)
    {
        if (!_built.TryGetValue(item, out var section))
        {
            _builder ??= new PropertyDefinitionBuilder();
            section = _builder.BuildSections([item])[0];
            _built.Add(item, section);
        }

        return section;
    }

    private IReadOnlyList<PropertySection> GeneratedSections()
    {
        var type = SharedType(Targets);
        if (type != _generatedFor)
        {
            _generatedFor = type;
            _dropped.AddRange(_generated);
            _builder ??= new PropertyDefinitionBuilder();
            _generated = type == null ? [] : _builder.BuildSections(type);
        }

        return _generated;
    }

    private static Type SharedType(IReadOnlyList<object> targets)
    {
        Type shared = null;
        foreach (var target in targets)
        {
            if (target == null)
            {
                continue;
            }

            var type = target.GetType();
            if (shared == null)
            {
                shared = type;
                continue;
            }

            while (!shared.IsAssignableFrom(type))
            {
                shared = shared.BaseType;
            }
        }

        return shared;
    }

    private void AddRow(Panel host, PropertyDefinition definition, IReadOnlyList<object> targets, int depth,
        String wanted, ref int at)
    {
        // WHAT THE LINE IS ABOUT, before anything is asked of it: a line can be written to depend on the state of the
        // very thing it describes - IsVisible="{Self Inspected.HasLabel}" - and that binding has to have been given its
        // object before its answer is read one line below. Nothing for a multiple selection: the line then stands for
        // several things at once and no single one of them is what it is about.
        definition.Inspected = targets.Count == 1 ? targets[0] : null;

        // ...and its own bindings settled RIGHT HERE, before the answer is read one line below. This one line only -
        // flushing the whole queue would push every pending binding in the application through, per row, per pass.
        if (BindingEngine.HasBindings(definition)) BindingEngine.RefreshBindings(definition);

        if (!definition.IsVisible) return;

        var searching = !String.IsNullOrEmpty(wanted);
        if (searching && !Answers(definition, wanted)) return;

        // The row already at this place, if there is one. Re-aimed rather than replaced - see the note where the panel
        // is kept.
        PropertyRow row;
        if (at < host.Children.Count && host.Children[at] is PropertyRow standing)
        {
            row = standing;
        }
        else
        {
            row = new PropertyRow();
            host.Children.Insert(Math.Min(at, host.Children.Count), row);
        }

        at++;

        row.Attach(this, definition, targets, depth * Indent);
        _rows.Add(row);

        if (definition is not CompositeProperty composite) return;

        // A folded composite opens while a search is running: its children are where the answer is, and leaving it shut
        // would hide what the search just found. A composite whose OWN name is the answer keeps all of its children.
        if (!composite.IsExpanded && !searching) return;

        var inside = searching && Carries(composite.Header as String, wanted) ? null : wanted;

        if (definition is ItemsProperty items)
        {
            AddItemRows(host, items, targets, depth, inside, ref at);
            return;
        }

        foreach (var child in composite.Children) AddRow(host, child, targets, depth + 1, inside, ref at);
    }

    // A group of rows PER ELEMENT: the same child definitions over and over, each time pointed at one item. Nothing is
    // copied - a definition already serves several targets at once, which is what multi-selection is - so a list of
    // twenty sockets costs twenty sets of rows and not twenty sets of definitions.
    private void AddItemRows(Panel host, ItemsProperty items, IReadOnlyList<object> targets, int depth, String wanted,
        ref int at)
    {
        // ONE object's list, not several. Two selected nodes have two lists of sockets and no common one, and showing
        // either of them under a heading that claims to be about both would be a lie an edit then acts on.
        if (targets.Count != 1 || ValueOf(targets[0], items) is not IEnumerable source) return;

        FollowItems(source);

        var index = 0;
        foreach (var item in source)
        {
            if (item == null) continue;

            index++;

            var label = new ItemHeaderLine
            {
                Header = Read(item, items.ItemHeader) ?? index.ToString(),
                ShowActionButton = items.ItemAction != null,
                ActionCommand = items.ItemAction,
                ActionIcon = items.ItemActionIcon,
                ActionTip = items.ItemActionTip
            };

            AddRow(host, label, new[] { item }, depth + 1, wanted, ref at);

            foreach (var child in items.Children) AddRow(host, child, new[] { item }, depth + 2, wanted, ref at);
        }
    }

    // The collections an ItemsProperty is showing, for as long as it is showing them. A socket added or dropped is a
    // ROW appearing or going, which no amount of re-reading values can do - the rows have to be built again.
    private void FollowItems(IEnumerable source)
    {
        if (source is not INotifyCollectionChanged live || _followed.Contains(live)) return;

        Follow((nameof(_followed), live), live, OnFollowedChanged);
        _followed.Add(live);
    }

    private void UnfollowItems()
    {
        foreach (var live in _followed)
        {
            Follow((nameof(_followed), live), null);
        }

        _followed.Clear();
    }

    private void OnFollowedChanged(object sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Watch()
    {
        Unwatch();

        foreach (var section in DisplayedSections())
        {
            foreach (var definition in section.Properties) Watch(definition);
        }
    }

    private void Unwatch()
    {
        foreach (var definition in _watched)
        {
            Follow((nameof(_watched), definition), null);
        }

        _watched.Clear();
    }

    private void Watch(PropertyDefinition definition)
    {
        definition.LayoutChanged += OnDefinitionLayoutChanged;
        definition.WordsChanged += OnDefinitionWordsChanged;
        if (definition is ImageSourceProperty picture) picture.Picked += OnPicturePicked;
        Follow((nameof(_watched), definition), () =>
        {
            definition.LayoutChanged -= OnDefinitionLayoutChanged;
            definition.WordsChanged -= OnDefinitionWordsChanged;
            if (definition is ImageSourceProperty shown)
            {
                shown.Picked -= OnPicturePicked;
            }
        });
        _watched.Add(definition);

        foreach (var child in definition.Children) Watch(child);
    }

    private void OnDefinitionLayoutChanged(object sender, EventArgs e) => Rebuild();

    private void OnDefinitionWordsChanged(object sender, EventArgs e) => Refresh(definition: (PropertyDefinition)sender);

    // A file chosen through the line's own button is written exactly as a typed path is: the same before/after report,
    // so an edit made by pointing at a file can be taken back like any other.
    private void OnPicturePicked(ImageSourceProperty definition, String path)
    {
        // THE FIRST ROW THIS LINE HAS, and then out. Writing a picture can change which lines apply - a plain surface
        // becomes one painted with a picture - so the panel is rebuilt from under this very walk, and every row after
        // this point belongs to the pass that has just been thrown away.
        for (var i = 0; i < _rows.Count; i++)
        {
            if (!ReferenceEquals(_rows[i].Definition, definition)) continue;

            Write(_rows[i], path);
            return;
        }
    }

    private void OnSectionsChanged(object sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private static void OnSelectedObjectChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e) =>
        (d as PropertyGrid)?.Rebuild();

    private static void OnSectionsSourceChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
    {
        if (d is not PropertyGrid grid)
        {
            return;
        }

        grid._sourceChanges.TakeThisThread();
        grid._sourceChanges.Clear();
        grid.Follow(SectionsSourceProperty, grid.SectionsSource as INotifyCollectionChanged, grid.OnSourceChanged);
        grid.Rebuild();
    }

    private void OnSourceChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (_sourceChanges.Defer(e)) return;
        Rebuild();
    }

    // Told to the rows that are already standing, rather than rebuilding them: a panel's manner can be changed while
    // somebody is looking at it, and every line answers the same way.
    private static void OnResetButtonChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
    {
        if (d is not PropertyGrid grid) return;

        foreach (var row in grid._rows) row.ResetButton = grid.ResetButton;
    }

    // The phrase changes with the language, and a row already saying it has to say the new one.
    private static void OnMixedTextChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
    {
        if (d is not PropertyGrid grid) return;

        foreach (var row in grid._rows) row.MarkMixed();
    }

    private static void OnNameColumnWidthChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
    {
        if (d is not PropertyGrid grid) return;

        var wanted = Math.Max(grid.MinNameColumnWidth, (double)e.NewValue);
        var room = grid.ActualWidth - grid.MinValueColumnWidth;
        if (room > grid.MinNameColumnWidth && wanted > room) wanted = room;

        if (Math.Abs(wanted - (double)e.NewValue) > 0.01)
        {
            grid.NameColumnWidth = wanted;
            return;
        }

        grid.FitNameWidth();
    }

    /// <summary>The name width every row is laid out with: <see cref="NameColumnWidth"/>, narrowed while the grid is too
    /// narrow for it, so the values keep <see cref="MinValueColumnWidth"/> beside the grip and the buttons.</summary>
    internal double EffectiveNameWidth { get; private set; } = 140.0;

    // One width for every row - the narrowest any row can give - or the columns turn into a staircase.
    private void FitNameWidth()
    {
        var wanted = Math.Max(0, NameColumnWidth);
        var room = double.MaxValue;
        foreach (var row in _rows) room = Math.Min(room, row.NameRoom());

        var fitted = room == double.MaxValue ? wanted : Math.Max(Math.Min(MinNameColumnWidth, wanted), Math.Min(wanted, room));
        if (Math.Abs(fitted - EffectiveNameWidth) < 0.5) return;

        EffectiveNameWidth = fitted;
        foreach (var row in _rows) row.ApplyNameWidth();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);

        // Only once the rows are laid out is it known how wide they stand.
        FitNameWidth();
        return size;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PropertyGridAutomationPeer(this);
}
