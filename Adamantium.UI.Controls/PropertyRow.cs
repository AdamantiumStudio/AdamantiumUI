using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>One inspector line drawn from a <see cref="PropertyDefinition"/>: name, grip and a live editor bound to each
/// inspected object. Parts: PART_Layout, PART_Name, PART_Grip, PART_Value, PART_Expander.</summary>
public class PropertyRow : Control
{
    public static readonly AdamantiumProperty IsReadOnlyProperty = AdamantiumProperty.Register(nameof(IsReadOnly),
        typeof(bool), typeof(PropertyRow),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty IndentProperty = AdamantiumProperty.Register(nameof(Indent),
        typeof(Double), typeof(PropertyRow),
        new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

    public static readonly AdamantiumProperty HasChildrenProperty = AdamantiumProperty.Register(nameof(HasChildren),
        typeof(bool), typeof(PropertyRow), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty IsExpandedProperty = AdamantiumProperty.Register(nameof(IsExpanded),
        typeof(bool), typeof(PropertyRow), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty ValueProperty = AdamantiumProperty.Register(nameof(Value),
        typeof(object), typeof(PropertyRow), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>The selected objects DISAGREE about this property. Shown as such rather than as an empty value - a
    /// blank field reads as "nothing", and the next keystroke would set four objects to what the user thought was one.</summary>
    public static readonly AdamantiumProperty IsMixedProperty = AdamantiumProperty.Register(nameof(IsMixed),
        typeof(bool), typeof(PropertyRow), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty IsModifiedProperty = AdamantiumProperty.Register(nameof(IsModified),
        typeof(bool), typeof(PropertyRow), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    /// <summary>Whether this row ends with the "..." button. Mirrored off the definition, like the rest of what the
    /// template triggers on - a template binds to the ROW, and the definition is not in its way.</summary>
    public static readonly AdamantiumProperty ShowActionButtonProperty = AdamantiumProperty.Register(
        nameof(ShowActionButton), typeof(bool), typeof(PropertyRow),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    /// <summary>When this row offers its reset button. The PANEL's manner, mirrored here for the same reason the line
    /// above is: a template triggers on the ROW, and the grid is not in its way.</summary>
    public static readonly AdamantiumProperty ResetButtonProperty = AdamantiumProperty.Register(nameof(ResetButton),
        typeof(ResetButtonState), typeof(PropertyRow),
        new PropertyMetadata(ResetButtonState.Always, PropertyMetadataOptions.AffectsRender));

    private readonly List<BoundValue> _values = new();
    private Grid _layout;
    private IInputComponent _grip;
    private IInputComponent _expander;
    private ButtonBase _action;
    private string _actionIcon;
    private string _actionTip;
    private ButtonBase _reset;
    private IInputComponent _editor;
    private ContentPresenter _valueHost;
    private ContentPresenter _nameHost;
    private bool _pendingEditor;
    private bool _writing;
    private bool _pushing;
    private bool _draggingGrip;
    private double _gripFrom;
    private double _widthFrom;

    public PropertyRow()
    {
        // The whole line toggles a composite. MouseDown because it bubbles (MouseLeftButtonDown is direct); rows without
        // children ignore it.
        MouseDown += OnRowPressed;
    }

    /// <summary>What this row draws.</summary>
    public PropertyDefinition Definition { get; internal set; }

    /// <summary>The grid it belongs to - the source of the shared name width and of the writes.</summary>
    public PropertyGrid Owner { get; internal set; }

    /// <summary>The objects the row reads and writes - one of them in the ordinary case, several while a multiple
    /// selection is being inspected.</summary>
    public IReadOnlyList<object> Targets { get; private set; } = Array.Empty<object>();

    /// <summary>The first of <see cref="Targets"/> - what a row of a single-object inspector is about.</summary>
    public object Target => Targets.Count > 0 ? Targets[0] : null;

    public bool IsReadOnly
    {
        get => GetValue<bool>(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>How far the NAME is pushed right - depth times the grid's indent. Only the name: the value half of a
    /// nested row still lines up with every other value, which is the whole point of a shared grip.</summary>
    public Double Indent
    {
        get => GetValue<Double>(IndentProperty);
        set => SetValue(IndentProperty, value);
    }

    public bool HasChildren
    {
        get => GetValue<bool>(HasChildrenProperty);
        set => SetValue(HasChildrenProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue<bool>(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>What the row currently holds.</summary>
    public object Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsMixed
    {
        get => GetValue<bool>(IsMixedProperty);
        set => SetValue(IsMixedProperty, value);
    }

    /// <summary>The type the property holds, read off the objects rather than off <see cref="Value"/> - which is null
    /// exactly when the objects disagree, and that is the case where a typed value still has to be converted before it
    /// can be written to all of them.</summary>
    public Type ValueType
    {
        get
        {
            foreach (var bound in _values)
            {
                if (bound.Value != null) return bound.Value.GetType();
            }

            return null;
        }
    }

    public bool ShowActionButton
    {
        get => GetValue<bool>(ShowActionButtonProperty);
        set => SetValue(ShowActionButtonProperty, value);
    }

    public ResetButtonState ResetButton
    {
        get => GetValue<ResetButtonState>(ResetButtonProperty);
        set => SetValue(ResetButtonProperty, value);
    }

    /// <summary>Whether what the objects hold is anything other than the property's default. The theme reads it to show
    /// the button that puts the default back - and the mark itself is worth having: an inspector of forty rows says at
    /// a glance which four were touched.</summary>
    public bool IsModified
    {
        get => GetValue<bool>(IsModifiedProperty);
        set => SetValue(IsModifiedProperty, value);
    }

    /// <summary>The live editor in the value half, or null on a read-only row.</summary>
    public IInputComponent Editor => _editor;

    /// <summary>Points the row at a definition and its objects. Called on creation and on every rebind.</summary>
    internal void Attach(PropertyGrid owner, PropertyDefinition definition, IReadOnlyList<object> targets, double indent)
    {
        // Same property, same objects - a REFRESH, not a rebind. Building the bindings again would cost the selection's
        // size twice over: every live value has to be let go one at a time, and letting one go is a search through the
        // rest. Measured on 50 000 objects, one write spent nearly five minutes there and nothing about what the row is
        // pointed at had changed.
        var rebind = !ReferenceEquals(Definition, definition) || !ReferenceEquals(Targets, targets);

        Owner = owner;
        Definition = definition;
        Targets = targets ?? Array.Empty<object>();
        Indent = indent;

        HasChildren = definition is CompositeProperty composite && composite.Children.Count > 0;
        IsExpanded = definition is CompositeProperty { IsExpanded: true };
        IsReadOnly = definition.IsReadOnly;
        ShowActionButton = definition.ShowActionButton;
        if (owner != null) ResetButton = owner.ResetButton;

        if (rebind) Bind();
        else Read();

        ApplyContent();
    }

    /// <summary>Writes what the editor holds. What the editors' own signals call, and what a test can call directly.</summary>
    public bool Commit()
    {
        if (_writing || Definition == null || IsReadOnly || _editor == null) return false;

        return Owner?.Write(this, Definition.ReadEditor(_editor)) ?? false;
    }

    // EVERY OBJECT'S OWN VALUE, not the row's. A write INTO a value edits the object that holds it and nothing else -
    // the row's copy belongs to the first of them, so a color written that way landed on one shape of a selection and
    // left the rest as they were.
    internal bool WriteInto(object edited)
    {
        if (_values.Count == 0 || Definition == null) return false;

        var all = true;

        foreach (var bound in _values)
        {
            if (!Definition.WriteInto(bound.Value, edited)) all = false;
        }

        return all;
    }

    /// <summary>Pushes one value into every object the row stands for, through their bindings.</summary>
    internal bool WriteValue(object value)
    {
        if (_values.Count == 0) return false;

        // Every object's write raises the very signal the row listens to, and answering each one re-reads ALL of them:
        // one value pushed to a selection of N costs N reads of N values. Measured on a selection of 50 000, a single
        // write took five minutes. The row already knows what it is writing, so their signals say nothing it does not
        // know - held off, and the row reads itself ONCE when the push is over.
        var landed = true;
        _pushing = true;
        try
        {
            foreach (var bound in _values)
            {
                if (bound.Write(value)) continue;

                landed = false;
                break;
            }
        }
        finally
        {
            _pushing = false;
        }

        Read();
        ApplyContent();
        return landed;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Unhook();

        _layout = GetTemplateChild("PART_Layout") as Grid;
        _grip = GetTemplateChild("PART_Grip") as IInputComponent;
        _expander = GetTemplateChild("PART_Expander") as IInputComponent;
        _valueHost = GetTemplateChild("PART_Value") as ContentPresenter;
        _nameHost = GetTemplateChild("PART_Name") as ContentPresenter;

        if (_grip != null)
        {
            _grip.MouseLeftButtonDown += OnGripPressed;
            if (_grip is UIComponent grip) grip.Cursor = Cursors.SizeEWE;
        }

        _action = GetTemplateChild("PART_Action") as ButtonBase;
        if (_action != null) _action.Click += OnActionPressed;

        _reset = GetTemplateChild("PART_Reset") as ButtonBase;
        if (_reset != null) _reset.Click += OnResetPressed;

        ApplyContent();
        ApplyNameWidth();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        Unhook();

        _layout = null;
        _grip = null;
        _expander = null;
        _valueHost = null;
        _nameHost = null;
    }

    /// <summary>Puts the shared name width into the layout. Called by the grid whenever the grip moves - one number for
    /// every row, or the columns of an inspector turn into a staircase.</summary>
    internal void ApplyNameWidth()
    {
        if (_layout == null || Owner == null || _layout.ColumnDefinitions.Count < 3) return;

        _layout.ColumnDefinitions[0].Width = new GridLength(Math.Max(0, Owner.NameColumnWidth));

        // A column definition changing its width dirties NOTHING by itself, so the path is dirtied by hand - starting
        // at the LAYOUT, not at the row: one valid element on the way stops the pass before it reaches the grid.
        for (IUIComponent node = _layout; node != null; node = node.VisualParent)
        {
            (node as IMeasurableComponent)?.InvalidateMeasure();
            if (ReferenceEquals(node, Owner)) break;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);

        // The editor is built by the presenter during measure, so it can only be found once a pass has run.
        if (_pendingEditor) HookEditor();

        return size;
    }

    // One live binding per inspected object. They are logical children so a value binding that reads an ancestor
    // instead of the object - which the author is free to write - resolves against the tree this row stands in.
    private void Bind()
    {
        foreach (var bound in _values)
        {
            bound.Changed -= OnBoundValueChanged;
            bound.Release();
            RemoveLogicalChild(bound);
        }

        _values.Clear();

        // NOTHING TO READ, so the row holds nothing. Rows are reused as the grid rebuilds, and one that simply returned
        // here went on showing the value of whatever it stood for last - a list's name wearing the count of the list
        // before it.
        if (Definition?.Binding == null)
        {
            Read();
            return;
        }

        foreach (var target in Targets)
        {
            var bound = new BoundValue();
            AddLogicalChild(bound);
            bound.PointAt(target, Definition.Binding);

            // Objects without this property are skipped, not counted as disagreeing.
            if (!bound.Reads)
            {
                bound.Release();
                RemoveLogicalChild(bound);
                continue;
            }

            bound.Changed += OnBoundValueChanged;
            _values.Add(bound);
        }

        Read();
    }

    // The common value, or nothing at all when the objects disagree.
    private void Read()
    {
        if (_values.Count == 0)
        {
            Value = null;
            IsMixed = false;
            IsModified = false;
            return;
        }

        var first = _values[0].Value;
        for (var i = 1; i < _values.Count; i++)
        {
            // The DEFINITION says what "the same" means: two brushes of one color are two instances, and comparing
            // them here would make the row report a difference nobody can see.
            if (Definition?.SameValue(_values[i].Value, first) ?? Equals(_values[i].Value, first)) continue;

            Value = null;
            IsMixed = true;

            // Objects that disagree cannot ALL be at the default - at most one of them is. So the row is modified, and
            // resetting it is the one edit that makes them agree again.
            IsModified = !IsReadOnly && (Definition?.HasDefault == true || Edited());
            return;
        }

        Value = first;
        IsMixed = false;

        // Never on a read-only row. Modified means differs from DefaultValue when one is given, otherwise a local value
        // on a component.
        IsModified = !IsReadOnly &&
            (Definition is { HasDefault: true } ? !Definition.SameValue(first, Default()) : Edited());
    }

    // Any of them: a selection where one object was edited and two were not is a selection with something to put back.
    private bool Edited()
    {
        foreach (var bound in _values)
        {
            if (bound.IsEdited) return true;
        }

        return false;
    }

    // The default AS THE PROPERTY WOULD HOLD IT. Written in markup it arrives as text - `DefaultValue="80"` is the
    // string "80", not the number - and comparing that with what the object holds would mark every such row as edited
    // the moment it was shown. The same conversion a write goes through, so the two agree about what the value is.
    private object Default()
    {
        var wanted = Definition?.DefaultValue;

        return Definition != null && Definition.TryConvert(wanted, ValueType, out var value) ? value : wanted;
    }

    private void OnBoundValueChanged(object sender, EventArgs e)
    {
        if (_pushing) return;

        Read();
        ApplyContent();
    }

    // What the action button LOOKS like, which is the only thing a person has to go on before pressing it. A line that
    // names a picture gets that picture and its own words; one that names neither keeps the three dots and "More" the
    // theme put there, which is the honest look for "there is more here".
    private void ApplyTip()
    {
        var tip = Definition.ValueAsTip ? Definition.TextOf(Value) : null;

        ToolTip = string.IsNullOrEmpty(tip) ? Definition.Description : tip;
    }

    private void ApplyAction()
    {
        if (_action == null) return;

        var icon = Definition.ActionIcon;

        // Only when changed: this runs on every refresh, and a new icon object would invalidate layout each time.
        if (_actionIcon == icon && _actionTip == Definition.ActionTip) return;

        _actionIcon = icon;
        _actionTip = Definition.ActionTip;

        ToolTipService.SetToolTip(_action, string.IsNullOrEmpty(_actionTip) ? "More" : _actionTip);

        if (string.IsNullOrEmpty(icon))
        {
            _action.Content = "...";
            return;
        }

        // LIVE against the theme, like every other picture in the application: a theme swap has to reach this one too,
        // and the line names a key precisely so the theme can answer differently.
        var image = new Image
        {
            Width = 11,
            Height = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        new Core.Resources.ObservableResource(icon).Apply(image, nameof(Image.Source));
        _action.Content = image;
    }

    private void Unhook()
    {
        if (_grip != null) _grip.MouseLeftButtonDown -= OnGripPressed;
        if (_action != null) _action.Click -= OnActionPressed;
        if (_reset != null) _reset.Click -= OnResetPressed;
        UnhookEditor();
    }

    private void ApplyContent()
    {
        if (Definition == null) return;

        if (_nameHost != null)
        {
            _nameHost.Content = Definition.Header;
            _nameHost.ContentTemplate = null;
        }

        ApplyAction();

        // The tip is the description, or the value for lines that ask for it (long paths); an empty value falls back to
        // the description.
        ApplyTip();

        // The whole line of a COMPOSITE opens it, so the whole line says so. On the ROW, because that is the target:
        // the chevron carried the hand all along and everything beside it did not, which is exactly the part of it
        // nobody thought to press.
        Cursor = HasChildren ? Cursors.Hand : Cursors.Arrow;

        if (_expander is MeasurableUIComponent strip) strip.Margin = new Thickness(Math.Max(0, Indent), 0, 0, 0);
        if (_valueHost == null) return;

        // A composite row has no value of its own; a read-only one shows text. Everything else gets its editor, and
        // keeps it - the editor is the row, not a state of it.
        var template = HasChildren
            ? null
            : IsReadOnly ? Definition.ValueFor() : Definition.EditorFor() ?? Definition.ValueFor();
        var rebuilt = !ReferenceEquals(_valueHost.ContentTemplate, template);

        if (rebuilt) UnhookEditor();

        _valueHost.ContentTemplate = template;

        // Null content builds no editor, so give empty content to keep one for disagreeing objects, unless the editor has
        // no empty state. An unset value still gets an editor.
        var nothing = template != null && (Definition.EditorCanShowNothing || !IsMixed);

        _valueHost.Content = HasChildren
            ? (Definition as CompositeProperty)?.Summary
            : Value ?? (nothing ? string.Empty : null);

        if (rebuilt || _editor == null) _pendingEditor = template != null && !IsReadOnly;
        else Fill();
    }

    private void Fill()
    {
        if (_editor == null || Definition == null) return;

        // Guarded, because filling the editor raises the very signals a user's change raises - and a write started from
        // a refresh would push the value the row has just read back into the model.
        _writing = true;
        try
        {
            Definition.PrepareEditor(_editor, Value);
            MarkMixed();
        }
        finally
        {
            _writing = false;
        }
    }

    // An empty editor says nothing on its own, and "nothing" is not what happened - the objects disagree, and for a
    // number it is not even a state the property can be in. So the editor's own prompt says which it is, and it goes
    // the moment they agree. ONLY then: a row whose objects hold one value shows that value like any other row.
    private void MarkMixed()
    {
        var prompt = IsMixed ? Owner?.MixedText : null;

        switch (_editor)
        {
            case NumericUpDown numeric: numeric.Placeholder = prompt; break;
            case TextBoxBase box: box.Placeholder = prompt; break;
            case DropDown drop: drop.Placeholder = prompt; break;
        }
    }

    private void HookEditor()
    {
        _pendingEditor = false;
        _editor = FindEditor(this);
        if (_editor == null) return;

        // One height for the whole inspector, stated rather than inherited: a control left to itself stands at whatever
        // its own content asks for, and three kinds of editor then stand at three different heights. A check box keeps
        // its square - it is a glyph, not a field.
        if (_editor is not ToggleButton && _editor is MeasurableUIComponent sized && Owner is { EditorHeight: > 0 })
            sized.Height = Owner.EditorHeight;

        Fill();

        // Each kind of editor says "the user changed me" its own way, and these are the four the inspector ships.
        if (_editor is TextBox box)
        {
            box.EnterPressed += OnEditorEntered;
            box.LostFocus += OnEditorLostFocus;
        }

        if (_editor is NumericUpDown numeric) numeric.ValueChanged += OnEditorValueChanged;
        if (_editor is DropDown drop) drop.SelectionChanged += OnEditorChosen;
        if (_editor is ToggleButton toggle) toggle.PropertyChanged += OnTogglePropertyChanged;
        if (_editor is ColorPickerButton swatch) swatch.PropertyChanged += OnSwatchPropertyChanged;
    }

    private void UnhookEditor()
    {
        if (_editor == null) return;

        if (_editor is TextBox box)
        {
            box.EnterPressed -= OnEditorEntered;
            box.LostFocus -= OnEditorLostFocus;
        }

        if (_editor is NumericUpDown numeric) numeric.ValueChanged -= OnEditorValueChanged;
        if (_editor is DropDown drop) drop.SelectionChanged -= OnEditorChosen;
        if (_editor is ToggleButton toggle) toggle.PropertyChanged -= OnTogglePropertyChanged;
        if (_editor is ColorPickerButton swatch) swatch.PropertyChanged -= OnSwatchPropertyChanged;

        _editor = null;
    }

    // The OUTERMOST editor, and by kind before by focusability: a spinner contains a text box, so a hunt for "the first
    // focusable thing" finds the box inside it and the row would then read text out of a control that deals in numbers.
    private static IInputComponent FindEditor(IUIComponent root)
    {
        foreach (var child in root.VisualChildren)
        {
            // The swatch is named here for the same reason the spinner is: it CONTAINS a picker full of fields and
            // sliders, and a hunt for "the first focusable thing" would come back with one of those.
            if (child is NumericUpDown or DropDown or ToggleButton or TextBox or ColorPickerButton)
                return (IInputComponent)child;
            if (FindEditor(child) is { } nested) return nested;
        }

        return Focusable(root);
    }

    private static IInputComponent Focusable(IUIComponent root)
    {
        foreach (var child in root.VisualChildren)
        {
            if (child is IInputComponent { Focusable: true } editor) return editor;
            if (Focusable(child) is { } nested) return nested;
        }

        return null;
    }

    private void OnEditorEntered(object sender, KeyEventArgs e)
    {
        if (Commit()) e.Handled = true;
    }

    private void OnEditorLostFocus(object sender, RoutedEventArgs e) => Commit();

    private void OnEditorValueChanged(object sender, EventArgs e) => Commit();

    private void OnEditorChosen(object sender, EventArgs e) => Commit();

    private void OnTogglePropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property != ToggleButton.IsCheckedProperty) return;

        // Not while the row fills itself: that raises the same signal as a click and would write back to every object.
        if (_writing) return;

        // A click on an indeterminate box lands on FALSE - that is the three-state cycle - while the row's rule for a
        // boolean the objects disagree on is TRUE, because leaving them disagreeing is the one thing nobody clicked
        // for. One rule whichever way the row is flipped.
        if (IsMixed && Owner != null) Owner.ToggleRow(this);
        else Commit();
    }

    // A color is chosen by DRAGGING inside the picker, so this fires all the way through the gesture rather than once
    // at the end. That is wanted: the object being inspected follows the pointer, which is the whole reason a color is
    // picked visually instead of typed.
    private void OnSwatchPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == ColorPickerButton.SelectedColorProperty) Commit();
    }

    /// <summary>Writes the property's default back to every object the row stands for, through the same path as an
    /// edit.</summary>
    public bool ResetToDefault()
    {
        if (Owner == null) return false;

        // What the markup SAYS is untouched, where it says anything - a plain number written back like any other edit.
        // Otherwise the written value is simply dropped, and what the object would hold without it comes back: the
        // theme's brush, the style's size. Writing a type's default over those would not be a reset - it would be one
        // more edit, and the one nobody asked for.
        return Definition is { HasDefault: true }
            ? Owner.Write(this, Definition.DefaultValue)
            : Owner.Reset(this);
    }

    // Drops what was written into every object the row stands for. Held off in the middle, exactly as a push is: each
    // object's clearing raises the signal the row listens to, and answering each one re-reads them all.
    internal bool ResetValues()
    {
        if (_values.Count == 0) return false;

        var dropped = false;
        _pushing = true;
        try
        {
            foreach (var bound in _values)
            {
                dropped |= bound.Reset();
            }
        }
        finally
        {
            _pushing = false;
        }

        if (dropped)
        {
            Read();
            ApplyContent();
        }

        return dropped;
    }

    /// <summary>Runs the definition's action with the row's objects, unless the definition names its own
    /// parameter.</summary>
    public bool RunAction()
    {
        if (Definition?.ActionCommand is not { } command) return false;

        var parameter = Definition.ActionCommandParameter ?? (Targets.Count == 1 ? Targets[0] : Targets);
        if (!command.CanExecute(parameter)) return false;

        command.Execute(parameter);
        return true;
    }

    private void OnActionPressed(object sender, RoutedEventArgs e)
    {
        if (RunAction()) e.Handled = true;
    }

    private void OnResetPressed(object sender, RoutedEventArgs e)
    {
        if (ResetToDefault()) e.Handled = true;
    }

    private void OnRowPressed(object sender, MouseButtonEventArgs e)
    {
        if (!HasChildren || e.ChangedButton != MouseButtons.Left || Owner == null) return;

        // ...unless the press landed on a BUTTON standing in the line. The whole line opens the row, and the buttons a
        // line offers sit on that line, so without this one press both does the thing and folds away what it was done
        // to. A button takes MouseLeftButtonDown, which is Direct, so nothing it marks handled is ever seen here.
        if (OnAButton(e.OriginalSource as IUIComponent)) return;

        Owner.ToggleComposite(this);
        e.Handled = true;
    }

    private bool OnAButton(IUIComponent from)
    {
        while (from != null && !ReferenceEquals(from, this))
        {
            if (from is Primitives.ButtonBase) return true;

            from = from.VisualParent;
        }

        return false;
    }

    private void OnGripPressed(object sender, MouseButtonEventArgs e)
    {
        if (Owner == null) return;

        _draggingGrip = true;
        _gripFrom = e.GetPosition(this).X;
        _widthFrom = Owner.NameColumnWidth;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(object sender, MouseEventArgs e)
    {
        base.OnMouseMove(sender, e);
        if (!_draggingGrip || Owner == null) return;

        Owner.NameColumnWidth = _widthFrom + (e.GetPosition(this).X - _gripFrom);
    }

    protected override void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(sender, e);
        if (!_draggingGrip) return;

        _draggingGrip = false;
        ReleaseMouseCapture();
    }
}
