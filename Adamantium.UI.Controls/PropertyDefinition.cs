using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Adamantium.Core.Commands;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

/// <summary>Describes one line of a <see cref="PropertyGrid"/>; <see cref="PropertyRow"/> draws it. Serves several targets
/// at once and joins its section's logical tree, so bindings resolve as on any element.</summary>
public abstract class PropertyDefinition : FundamentalUIComponent
{
    public static readonly AdamantiumProperty HeaderProperty = AdamantiumProperty.Register(nameof(Header),
        typeof(object), typeof(PropertyDefinition), new PropertyMetadata(null, OnWordsChanged));

    /// <summary>What this line reads and writes - a REAL binding against the object being inspected, with converters,
    /// formats, modes and <c>MultiBinding</c>. Not a member name, which can carry none of them.</summary>
    public static readonly AdamantiumProperty BindingProperty = AdamantiumProperty.Register(nameof(Binding),
        typeof(BindingBase), typeof(PropertyDefinition), new PropertyMetadata(null));

    public static readonly AdamantiumProperty DescriptionProperty = AdamantiumProperty.Register(nameof(Description),
        typeof(String), typeof(PropertyDefinition), new PropertyMetadata(null, OnWordsChanged));

    /// <summary>Whether the row's tip is its own VALUE rather than the description. For a line holding something longer
    /// than its cell - a path, an address - the one question asked of it is what it actually says, and the description
    /// is a sentence the reader has already read. Falls back to the description while the value is empty.</summary>
    public static readonly AdamantiumProperty ValueAsTipProperty = AdamantiumProperty.Register(nameof(ValueAsTip),
        typeof(bool), typeof(PropertyDefinition), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsReadOnlyProperty = AdamantiumProperty.Register(nameof(IsReadOnly),
        typeof(bool), typeof(PropertyDefinition), new PropertyMetadata(false));

    /// <summary>Whether the line is shown at all. Bindable like everything else here, which is what an inspector with a
    /// "show advanced" switch is made of.</summary>
    public static readonly AdamantiumProperty IsVisibleProperty = AdamantiumProperty.Register(nameof(IsVisible),
        typeof(bool), typeof(PropertyDefinition), new PropertyMetadata(true, OnIsVisibleChanged));

    /// <summary>Whether the line ends with the "..." button. OFF by default: a button on every line of an inspector is
    /// a column of buttons that mostly do nothing, and a line that offers one had better mean it.</summary>
    public static readonly AdamantiumProperty ShowActionButtonProperty = AdamantiumProperty.Register(
        nameof(ShowActionButton), typeof(bool), typeof(PropertyDefinition),
        new PropertyMetadata(false, OnShowActionButtonChanged));

    /// <summary>What the "..." button runs. The editor in the value cell says what the property IS; this is for
    /// everything a cell cannot hold - a file to pick, a longer form to open, a value to be computed from elsewhere -
    /// and what it does is the application's business, which is why it is a command and not an event on the control.
    /// </summary>
    public static readonly AdamantiumProperty ActionCommandProperty = AdamantiumProperty.Register(nameof(ActionCommand),
        typeof(ICommand), typeof(PropertyDefinition), new PropertyMetadata(null));

    /// <summary>The property's untouched value; a row holding anything else offers to reset to it. Null is a valid
    /// default, so the slot starts unset and <see cref="HasDefault"/> tracks whether one was given.</summary>
    public static readonly AdamantiumProperty DefaultValueProperty = AdamantiumProperty.Register(nameof(DefaultValue),
        typeof(object), typeof(PropertyDefinition),
        new PropertyMetadata(AdamantiumProperty.UnsetValue, OnDefaultValueChanged));

    /// <summary>Whether a default was ever given. The definition keeps it; a row reads it to know whether resetting is
    /// something it can offer at all.</summary>
    public static readonly AdamantiumProperty HasDefaultProperty = AdamantiumProperty.Register(nameof(HasDefault),
        typeof(bool), typeof(PropertyDefinition), new PropertyMetadata(false));

    /// <summary>What the command is given; null means the objects the line is pointed at.</summary>
    public static readonly AdamantiumProperty ActionCommandParameterProperty = AdamantiumProperty.Register(
        nameof(ActionCommandParameter), typeof(object), typeof(PropertyDefinition), new PropertyMetadata(null));

    /// <summary>Theme key of the action button's icon; empty keeps the "..." used for "there is more". An immediate
    /// action should show what it does.</summary>
    public static readonly AdamantiumProperty ActionIconProperty = AdamantiumProperty.Register(nameof(ActionIcon),
        typeof(String), typeof(PropertyDefinition), new PropertyMetadata(null, OnWordsChanged));

    /// <summary>What the action button says it will do, on hover. Empty keeps the theme's "More", and it is a promise
    /// the button has to keep.</summary>
    public static readonly AdamantiumProperty ActionTipProperty = AdamantiumProperty.Register(nameof(ActionTip),
        typeof(String), typeof(PropertyDefinition), new PropertyMetadata(null, OnWordsChanged));

    /// <summary>What the value looks like when nobody is editing it. Null means the default look for its kind.</summary>
    public static readonly AdamantiumProperty ValueTemplateProperty = AdamantiumProperty.Register(nameof(ValueTemplate),
        typeof(DataTemplate), typeof(PropertyDefinition), new PropertyMetadata(null));

    /// <summary>What it turns into while it IS being edited. Null means the default editor for its kind.</summary>
    public static readonly AdamantiumProperty EditorTemplateProperty = AdamantiumProperty.Register(
        nameof(EditorTemplate), typeof(DataTemplate), typeof(PropertyDefinition), new PropertyMetadata(null));

    protected PropertyDefinition()
    {
        Children.CollectionChanged += OnChildrenChanged;
    }

    /// <summary>Raised when something a row has to redraw itself for changed - visibility, or the children collection.</summary>
    internal event EventHandler LayoutChanged;

    // Says the line has to be built again. Here rather than in each definition because an event can only be raised by
    // the type that declares it, and what a kind of line changes shape for is the kind's own business.
    private protected void Relayout() => LayoutChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised when what a row SAYS for this line changed - its name, tip or button - and the row only has to
    /// read it again. A language switch changes every one of them at once, and rebuilding the panel for that would be
    /// a rebuild per line.</summary>
    internal event EventHandler WordsChanged;

    public object Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public BindingBase Binding
    {
        get => GetValue<BindingBase>(BindingProperty);
        set => SetValue(BindingProperty, value);
    }

    /// <summary>The single object this line describes, or null with several selected; lets a line bind to its target's
    /// state, e.g. <c>IsVisible="{Self Inspected.HasLabel}"</c>. DataContext stays the panel's view-model.</summary>
    public static readonly AdamantiumProperty InspectedProperty = AdamantiumProperty.Register(nameof(Inspected),
        typeof(object), typeof(PropertyDefinition), new PropertyMetadata(null));

    public object Inspected
    {
        get => GetValue(InspectedProperty);
        internal set => SetValue(InspectedProperty, value);
    }

    /// <summary>One line about what the property means - a tooltip, and the status line in an editor.</summary>
    public String Description
    {
        get => GetValue<String>(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool ValueAsTip
    {
        get => GetValue<bool>(ValueAsTipProperty);
        set => SetValue(ValueAsTipProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue<bool>(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool IsVisible
    {
        get => GetValue<bool>(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    public bool ShowActionButton
    {
        get => GetValue<bool>(ShowActionButtonProperty);
        set => SetValue(ShowActionButtonProperty, value);
    }

    public ICommand ActionCommand
    {
        get => GetValue<ICommand>(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public String ActionIcon
    {
        get => GetValue<String>(ActionIconProperty);
        set => SetValue(ActionIconProperty, value);
    }

    public String ActionTip
    {
        get => GetValue<String>(ActionTipProperty);
        set => SetValue(ActionTipProperty, value);
    }

    public object ActionCommandParameter
    {
        get => GetValue(ActionCommandParameterProperty);
        set => SetValue(ActionCommandParameterProperty, value);
    }

    public object DefaultValue
    {
        get
        {
            var value = GetValue(DefaultValueProperty);
            return ReferenceEquals(value, AdamantiumProperty.UnsetValue) ? null : value;
        }
        set => SetValue(DefaultValueProperty, value);
    }

    public bool HasDefault
    {
        get => GetValue<bool>(HasDefaultProperty);
        set => SetValue(HasDefaultProperty, value);
    }

    public DataTemplate ValueTemplate
    {
        get => GetValue<DataTemplate>(ValueTemplateProperty);
        set => SetValue(ValueTemplateProperty, value);
    }

    public DataTemplate EditorTemplate
    {
        get => GetValue<DataTemplate>(EditorTemplateProperty);
        set => SetValue(EditorTemplateProperty, value);
    }

    /// <summary>Properties nested under this one. A vector is one line with three children; a plain property has none.
    /// [Content], so they are written as the child elements they look like.</summary>
    [Content]
    public PropertyDefinitions Children { get; } = new();

    /// <summary>The editor this line puts in the value cell. Null means it cannot be edited through the UI.</summary>
    protected internal virtual DataTemplate DefaultEditorTemplate => null;

    /// <summary>What the value looks like when it CANNOT be edited. Null means plain text.</summary>
    protected internal virtual DataTemplate DefaultValueTemplate => null;

    protected internal DataTemplate EditorFor() => EditorTemplate ?? DefaultEditorTemplate;

    /// <summary>Told the type a row's objects hold, just before that row asks for its editor - for a line whose editor
    /// depends on it. A line shown in several rows is told before each.</summary>
    protected internal virtual void Meet(Type valueType) { }

    protected internal DataTemplate ValueFor() => ValueTemplate ?? DefaultValueTemplate;

    /// <summary>Fills the editor with the value. Called whenever the row is (re)bound - the editor is LIVE, so this
    /// runs on every refresh, not once at the start of an edit.</summary>
    protected internal virtual void PrepareEditor(IUIComponent editor, object value) { }

    /// <summary>Reads back what the editor holds. Null means "nothing to say" and the previous value stands.</summary>
    protected internal virtual object ReadEditor(IUIComponent editor) => null;

    /// <summary>Turns what the editor produced into what the property will take - a string into an int, a chosen item
    /// into its underlying value. Returns false to refuse, which leaves the row in edit with what was typed.</summary>
    protected internal virtual bool TryConvert(object edited, Type target, out object value) =>
        BoundValue.TryConvert(edited, target, out value);

    /// <summary>...knowing the value the objects hold now, or null while they disagree - for a line whose text does not
    /// say all of its value, which keeps the rest from it.</summary>
    protected internal virtual bool TryConvert(object edited, Type target, object current, out object value) =>
        TryConvert(edited, target, out value);

    /// <summary>Whether two selected objects hold the same value, so a multi-selection row shows it. Override for object
    /// values (brushes) without giving the type an equality the render cache relies on not having.</summary>
    protected internal virtual bool SameValue(object left, object right) => Equals(left, right);

    /// <summary>Whether what the objects hold is the line's default - <see cref="SameValue"/> unless the default is a
    /// moment that each object holds in an offset of its own.</summary>
    protected internal virtual bool SameAsDefault(object value, object fallback) => SameValue(value, fallback);

    /// <summary>Whether objects holding different values can all be at <paramref name="fallback"/> - a date or a time,
    /// held by each at an offset or of a kind of its own. False by default.</summary>
    protected internal virtual bool IsMetInManyWays(object fallback) => false;

    /// <summary>Turns <see cref="DefaultValue"/> into what the property holds. Text is read as markup writes it - in no
    /// language - so the default does not move with the language.</summary>
    protected internal virtual bool TryReadDefault(Type target, out object value) =>
        DefaultValue is string && target != null && !target.IsInstanceOfType(DefaultValue)
            && BoundValue.TryConvert(DefaultValue, target, true, out value)
        || TryConvert(DefaultValue, target, out value);

    /// <summary>The value as a person would read it, which is what <see cref="ValueAsTip"/> shows. A number or a string
    /// says itself; a value that is an OBJECT does not - a brush painted from a file has a file to name, and its type's
    /// own wording is not it.</summary>
    protected internal virtual String TextOf(object value) => value?.ToString();

    /// <summary>The value as automation reads it: the invariant culture's text by default; a line whose value has a form
    /// of its own, a date or a length, reads in that form, and that text written back keeps what the form does not show.</summary>
    protected internal virtual String AutomationTextOf(object value) =>
        System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Whether the editor can show no value while staying usable, for rows of disagreeing objects; a color
    /// swatch cannot, so its row stays blank.</summary>
    protected internal virtual bool EditorCanShowNothing => true;

    /// <summary>Writes the edit into the current value in place (e.g. recolors a shared brush) and returns true; false
    /// means the value is replaced.</summary>
    protected internal virtual bool WriteInto(object current, object edited) => false;

    private static void OnIsVisibleChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        (component as PropertyDefinition)?.LayoutChanged?.Invoke(component, EventArgs.Empty);

    private static void OnWordsChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        (component as PropertyDefinition)?.WordsChanged?.Invoke(component, EventArgs.Empty);

    // The row reads this when it is bound, so a line that gains its button while the inspector is open has to be told
    // to look again - the same reason IsVisible says so.
    private static void OnShowActionButtonChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        (component as PropertyDefinition)?.LayoutChanged?.Invoke(component, EventArgs.Empty);

    // Being ASSIGNED is what gives a property a default, whatever it was assigned - null included.
    private static void OnDefaultValueChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not PropertyDefinition definition) return;

        definition.HasDefault = !ReferenceEquals(e.NewValue, AdamantiumProperty.UnsetValue);
        definition.LayoutChanged?.Invoke(component, EventArgs.Empty);
    }

    private void OnChildrenChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // A child is a definition like any other, so it JOINS the tree too - otherwise the three lines of a vector
        // would be the only ones in the inspector whose bindings resolve against nothing.
        foreach (var child in e.OldItems?.OfType<PropertyDefinition>() ?? Enumerable.Empty<PropertyDefinition>())
            RemoveLogicalChild(child);

        foreach (var child in e.NewItems?.OfType<PropertyDefinition>() ?? Enumerable.Empty<PropertyDefinition>())
            AddLogicalChild(child);

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

}

/// <summary>Text. The plain case, and the one everything else is measured against.</summary>
public class StringProperty : PropertyDefinition
{
    private DataTemplate _editor;

    protected internal override DataTemplate DefaultEditorTemplate =>
        _editor ??= new DataTemplate(() => new TemplateResult { RootComponent = PropertyEditors.Field() });

    protected internal override void PrepareEditor(IUIComponent editor, object value)
    {
        if (editor is TextBox box) box.Text = value?.ToString() ?? String.Empty;
    }

    protected internal override object ReadEditor(IUIComponent editor) => (editor as TextBox)?.Text;
}

/// <summary>A number, with the bounds and the step the property actually has - which is why they live here and not in
/// the template: a rotation is 0..360 and a scale is not.</summary>
public class NumericProperty : PropertyDefinition
{
    public static readonly AdamantiumProperty MinimumProperty = AdamantiumProperty.Register(nameof(Minimum),
        typeof(Double), typeof(NumericProperty), new PropertyMetadata(Double.MinValue));

    public static readonly AdamantiumProperty MaximumProperty = AdamantiumProperty.Register(nameof(Maximum),
        typeof(Double), typeof(NumericProperty), new PropertyMetadata(Double.MaxValue));

    public static readonly AdamantiumProperty StepProperty = AdamantiumProperty.Register(nameof(Step),
        typeof(Double), typeof(NumericProperty), new PropertyMetadata(1.0));

    public static readonly AdamantiumProperty DecimalsProperty = AdamantiumProperty.Register(nameof(Decimals),
        typeof(Int32), typeof(NumericProperty), new PropertyMetadata(3));

    /// <summary>Whether the line ends with the up and down buttons. ON by default, because a number a person nudges is
    /// what a stepper is for - but they cost width, and in a narrow inspector that width comes out of the field the
    /// number is actually read in. A panel that is short of room turns them off and keeps the digits.</summary>
    public static readonly AdamantiumProperty ShowButtonsProperty = AdamantiumProperty.Register(nameof(ShowButtons),
        typeof(Boolean), typeof(NumericProperty), new PropertyMetadata(true));

    /// <summary>Which side of the number the steppers sit on; left by default, since the row's reset and "..." buttons
    /// come and go on the right and would shift them.</summary>
    public static readonly AdamantiumProperty ButtonsPlacementProperty = AdamantiumProperty.Register(
        nameof(ButtonsPlacement), typeof(NumericButtonsPlacement), typeof(NumericProperty),
        new PropertyMetadata(NumericButtonsPlacement.Left));

    private DataTemplate _editor;

    public Double Minimum
    {
        get => GetValue<Double>(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public Double Maximum
    {
        get => GetValue<Double>(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Double Step
    {
        get => GetValue<Double>(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>How many decimals the editor shows. Not how many the VALUE has - the property keeps its own precision.</summary>
    public Int32 Decimals
    {
        get => GetValue<Int32>(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    public Boolean ShowButtons
    {
        get => GetValue<Boolean>(ShowButtonsProperty);
        set => SetValue(ShowButtonsProperty, value);
    }

    public NumericButtonsPlacement ButtonsPlacement
    {
        get => GetValue<NumericButtonsPlacement>(ButtonsPlacementProperty);
        set => SetValue(ButtonsPlacementProperty, value);
    }

    protected internal override DataTemplate DefaultEditorTemplate =>
        _editor ??= new DataTemplate(() => new TemplateResult { RootComponent = PropertyEditors.Number() });

    protected internal override void PrepareEditor(IUIComponent editor, object value)
    {
        if (editor is not NumericUpDown numeric) return;

        numeric.Minimum = Minimum;
        numeric.Maximum = Maximum;
        numeric.SmallChange = Step;
        numeric.AreButtonsVisible = ShowButtons;
        numeric.ButtonsPlacement = ButtonsPlacement;

        // "0.###" rather than "N3": limits noise like 182.99999999999997 while a round number stays round.
        numeric.StringFormat = Decimals > 0 ? "0." + new String('#', Decimals) : "0";
        // NULL, not zero, when there is nothing to show - the objects disagree. A zero here would be a number neither
        // of them holds, which is worse than an empty field: it reads as an answer.
        numeric.Value = value is IConvertible convertible
            ? Convert.ToDouble(convertible, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    protected internal override object ReadEditor(IUIComponent editor) => (editor as NumericUpDown)?.Value;
}

/// <summary>A flag: a live box, flipped by one click like every other check box on the page.</summary>
public class BooleanProperty : PropertyDefinition
{
    private DataTemplate _display;
    private DataTemplate _editor;

    protected internal override DataTemplate DefaultValueTemplate => _display ??= PropertyEditors.Box(live: false);

    protected internal override DataTemplate DefaultEditorTemplate => _editor ??= PropertyEditors.Box(live: true);

    protected internal override void PrepareEditor(IUIComponent editor, object value)
    {
        if (editor is not ToggleButton toggle) return;

        // INDETERMINATE when the objects disagree: an unticked box would say they are all off, and half of them are on.
        // Three-state only while that is the case, so an ordinary row is an ordinary two-state box.
        toggle.IsThreeState = value is not bool;
        toggle.IsChecked = value as bool?;
    }

    protected internal override object ReadEditor(IUIComponent editor) => (editor as ToggleButton)?.IsChecked;
}

/// <summary>A choice from a list: an enum, or whatever the application offers. An enum fills the list by itself.</summary>
public class ChoiceProperty : PropertyDefinition
{
    public static readonly AdamantiumProperty ItemsSourceProperty = AdamantiumProperty.Register(nameof(ItemsSource),
        typeof(IEnumerable), typeof(ChoiceProperty), new PropertyMetadata(null, OnChoicesChanged));

    public static readonly AdamantiumProperty EnumTypeProperty = AdamantiumProperty.Register(nameof(EnumType),
        typeof(Type), typeof(ChoiceProperty), new PropertyMetadata(null, OnChoicesChanged));

    /// <summary>The property of an item that IS the value, when the list is of objects and the line binds one of their
    /// fields - a catalog of node kinds against a node's own word for its kind. Empty means the item itself is the
    /// value, which is the ordinary case.</summary>
    public static readonly AdamantiumProperty ValuePathProperty = AdamantiumProperty.Register(nameof(ValuePath),
        typeof(String), typeof(ChoiceProperty), new PropertyMetadata(null, OnChoicesChanged));

    // The list a line offers ARRIVES - a page states its catalog with a binding, and a binding is pushed after the
    // panel holding the line has been built. A row made over an empty list keeps offering nothing and shows nothing for
    // the value it holds, which is a drop-down standing blank beside an object that plainly has one.
    private static void OnChoicesChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        (component as ChoiceProperty)?.Relayout();

    public String ValuePath
    {
        get => GetValue<String>(ValuePathProperty);
        set => SetValue(ValuePathProperty, value);
    }

    private DataTemplate _editor;

    public IEnumerable ItemsSource
    {
        get => GetValue<IEnumerable>(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Fills the list from an enum's values, so an enum property needs no list of its own.</summary>
    public Type EnumType
    {
        get => GetValue<Type>(EnumTypeProperty);
        set => SetValue(EnumTypeProperty, value);
    }

    protected internal override DataTemplate DefaultEditorTemplate =>
        _editor ??= new DataTemplate(() => new TemplateResult { RootComponent = PropertyEditors.Choice() });

    /// <summary>The choices for this property: the explicit list, else the enum's values, else nothing. The same list
    /// each time while the enum stays, so an editor refilled with it keeps its items.</summary>
    public IEnumerable Choices()
    {
        if (ItemsSource != null) return ItemsSource;
        if (EnumType is not { IsEnum: true } type) return null;

        if (_enumValuesOf != type)
        {
            _enumValues = Enum.GetValues(type);
            _enumValuesOf = type;
        }

        return _enumValues;
    }

    private Array _enumValues;
    private Type _enumValuesOf;

    protected internal override void PrepareEditor(IUIComponent editor, object value)
    {
        if (editor is not DropDown drop) return;

        drop.ItemsSource = Choices();
        ShowIn(drop, value);
    }

    protected internal override object ReadEditor(IUIComponent editor) => ReadFrom(editor as Selector);

    // The selector finds the item whose ValuePath reads as the value; an editor that brings its own choices keeps them.
    private protected void ShowIn(Selector selector, object value)
    {
        if (!String.IsNullOrEmpty(ValuePath))
        {
            selector.SelectedValuePath = ValuePath;
        }

        selector.SelectedValue = value;
        selector.SelectValue(value);
    }

    private protected static object ReadFrom(Selector selector) =>
        selector?.SelectedItem == null ? null : selector.SelectedValue;
}

/// <summary>A choice from a few, all in sight as the segments of a <see cref="SegmentedControl"/> rather than hidden in a
/// drop-down. The choices come as a <see cref="ChoiceProperty"/>'s do, or from an <see cref="PropertyDefinition.EditorTemplate"/>
/// whose segments are written in it - pictures standing for values, with <see cref="ChoiceProperty.ValuePath"/> naming
/// the value of each.</summary>
public class SegmentedChoiceProperty : ChoiceProperty
{
    /// <summary>What a segment shows of its choice; null shows the choice as text.</summary>
    public static readonly AdamantiumProperty ItemTemplateProperty = AdamantiumProperty.Register(nameof(ItemTemplate),
        typeof(DataTemplate), typeof(SegmentedChoiceProperty), new PropertyMetadata(null));

    private DataTemplate _editor;

    public DataTemplate ItemTemplate
    {
        get => GetValue<DataTemplate>(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    protected internal override DataTemplate DefaultEditorTemplate =>
        _editor ??= new DataTemplate(() => new TemplateResult { RootComponent = PropertyEditors.Segments() });

    protected internal override void PrepareEditor(IUIComponent editor, object value)
    {
        if (editor is not SegmentedControl segments)
        {
            return;
        }

        if (ItemTemplate != null)
        {
            segments.ItemTemplate = ItemTemplate;
        }

        if (Choices() is { } choices)
        {
            segments.ItemsSource = choices;
        }

        ShowIn(segments, value);
    }

    protected internal override object ReadEditor(IUIComponent editor) => ReadFrom(editor as SegmentedControl);
}

/// <summary>A <see cref="Color"/>, edited by the swatch button that opens the full picker. A color is the one value
/// nobody can type: "#3A6EA5" says nothing to the eye and three numbers say less, so the editor has to SHOW it.</summary>
public class ColorProperty : PropertyDefinition
{
    private DataTemplate _editor;

    protected internal override DataTemplate DefaultEditorTemplate =>
        _editor ??= new DataTemplate(() => new TemplateResult { RootComponent = PropertyEditors.Swatch() });

    protected internal override bool EditorCanShowNothing => false;

    protected internal override void PrepareEditor(IUIComponent editor, object value)
    {
        if (editor is ColorPickerButton swatch && value is Color color) swatch.SelectedColor = color;
    }

    protected internal override object ReadEditor(IUIComponent editor) => (editor as ColorPickerButton)?.SelectedColor;
}

/// <summary>A <see cref="SolidColorBrush"/> edited with the color swatch. The color is written into the object's own
/// brush; a frozen or shared one is replaced instead.</summary>
public class SolidColorBrushProperty : PropertyDefinition
{
    private DataTemplate _editor;

    protected internal override DataTemplate DefaultEditorTemplate =>
        _editor ??= new DataTemplate(() => new TemplateResult { RootComponent = PropertyEditors.Swatch() });

    // A SWATCH SHOWING NOTHING IS STILL A SWATCH TO PRESS. Refused, the line lost its editor the moment the selected
    // objects disagreed - and putting ONE color on several is exactly what a color line of a multiple selection is
    // for. What it must not do is show a color none of them holds, which is what IsIndeterminate says.
    protected internal override bool EditorCanShowNothing => true;

    protected internal override void PrepareEditor(IUIComponent editor, object value)
    {
        if (editor is not ColorPickerButton swatch) return;

        swatch.IsIndeterminate = value is not SolidColorBrush;

        if (value is SolidColorBrush brush) swatch.SelectedColor = brush.Color;
    }

    protected internal override object ReadEditor(IUIComponent editor) => (editor as ColorPickerButton)?.SelectedColor;

    // What the line shows of a brush is its COLOR, so that is what "the same" means here. Two objects each holding
    // their own brush of the same color agree as far as this line is concerned, and a row reporting otherwise would be
    // reporting a difference nobody can see.
    protected internal override bool SameValue(object left, object right) =>
        left is SolidColorBrush first && right is SolidColorBrush second
            ? first.Color == second.Color
            : base.SameValue(left, right);

    protected internal override bool WriteInto(object current, object edited)
    {
        // Never into a shared (theme) brush, which would recolor everything using it; that edit gets a new brush.
        if (current is not SolidColorBrush { IsFrozen: false, IsShared: false } brush ||
            edited is not Color color)
        {
            return false;
        }

        brush.Color = color;
        return true;
    }

    // Reached only when the brush would not take the color - it is frozen, it belongs to the theme, or there was no
    // brush there at all. A new one then, because refusing here would be the line quietly doing nothing.
    protected internal override bool TryConvert(object edited, Type target, out object value)
    {
        if (edited is Color color)
        {
            value = new SolidColorBrush(color);
            return true;
        }

        return base.TryConvert(edited, target, out value);
    }
}

/// <summary>A date, a time or both - <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="DateOnly"/> or
/// <see cref="TimeOnly"/> - in the picker for the parts <see cref="Format"/> names: a calendar for a date, the times of
/// a day for a time, both for both. What is picked goes back as the type the property holds, each object's own offset or
/// kind kept.</summary>
public class DateTimeProperty : PropertyDefinition
{
    public static readonly AdamantiumProperty FormatProperty = AdamantiumProperty.Register(nameof(Format),
        typeof(DateTimeFieldFormat?), typeof(DateTimeProperty), new PropertyMetadata(null, OnFormatChanged));

    public static readonly AdamantiumProperty CustomFormatProperty = AdamantiumProperty.Register(nameof(CustomFormat),
        typeof(String), typeof(DateTimeProperty), new PropertyMetadata(null, OnFormatChanged));

    private readonly Dictionary<DateTimeFieldFormat, DataTemplate> _editors = new();
    private DataTemplate _display;
    private DateTimeFieldFormat _built = DateTimeFieldFormat.ShortDateTime;

    /// <summary>Which parts are shown and edited, as in <see cref="DateTimeField.Format"/>. Unset picks them by what the
    /// line holds: the short date for a <see cref="DateOnly"/>, the short time for a <see cref="TimeOnly"/>, both
    /// otherwise.</summary>
    public DateTimeFieldFormat? Format
    {
        get => GetValue<DateTimeFieldFormat?>(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    /// <summary>A .NET date and time pattern used when <see cref="Format"/> is <see cref="DateTimeFieldFormat.Custom"/>.</summary>
    public String CustomFormat
    {
        get => GetValue<String>(CustomFormatProperty);
        set => SetValue(CustomFormatProperty, value);
    }

    protected internal override DataTemplate DefaultEditorTemplate
    {
        get
        {
            var format = Format ?? _built;
            if (_editors.TryGetValue(format, out var editor)) return editor;

            editor = new DataTemplate(() => new TemplateResult
            {
                RootComponent = PropertyEditors.Picker(DateTimeValues.PickerFor(format, CustomFormat))
            });
            _editors[format] = editor;
            return editor;
        }
    }

    protected internal override void Meet(Type valueType)
    {
        if (valueType != null && DateTimeValues.IsDate(valueType)) _built = DateTimeValues.FormatFor(valueType);
    }

    protected internal override DataTemplate DefaultValueTemplate =>
        _display ??= new DataTemplate(() =>
        {
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var result = new TemplateResult { RootComponent = text };
            result.AddBinding(text, "Text", new Binding { Mode = BindingMode.OneWay, Converter = new Shown(this) });
            return result;
        });

    protected internal override void PrepareEditor(IUIComponent editor, object value)
    {
        if (editor is DateTimeField field) field.Value = DateTimeValues.ToDateTime(value);
    }

    protected internal override object ReadEditor(IUIComponent editor)
    {
        var field = editor as DateTimeField;
        field?.FinishTyping();
        return field?.Value;
    }

    protected internal sealed override bool TryConvert(object edited, Type target, out object value) =>
        TryConvert(edited, target, null, out value);

    protected internal override bool TryConvert(object edited, Type target, object current, out object value)
    {
        if (edited is string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return base.TryConvert(null, target, out value);

            if (!DateTimeValues.TryParseMoment(text, FormatOf(current), CustomFormat, Languages.Culture, current, out var read))
            {
                value = null;
                return false;
            }

            edited = read;
        }

        if (edited is DateTime { Kind: DateTimeKind.Unspecified } clock)
        {
            value = DateTimeValues.InFrameOf(clock, current);
            return true;
        }

        if (edited is DateTime or DateTimeOffset)
        {
            value = edited;
            return true;
        }

        return base.TryConvert(edited, target, out value);
    }

    protected internal override bool SameAsDefault(object value, object fallback) =>
        value is not null && DateTimeValues.AsMoment(fallback) is { } moment ? DateTimeValues.SameInstant(value, moment)
        : value is not null && DateTimeValues.ToDateTime(value) is { } clock && DateTimeValues.ToDateTime(fallback) is { } said
            ? clock == said
            : SameValue(value, fallback);

    protected internal override bool IsMetInManyWays(object fallback) => DateTimeValues.ToDateTime(fallback) != null;

    protected internal override bool TryReadDefault(Type target, out object value)
    {
        if (DefaultValue is string text && !string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return DateTimeValues.TryParseMoment(text, FormatOf(null), CustomFormat,
                       System.Globalization.CultureInfo.InvariantCulture, null,
                       out var read) && TryConvert(read, target, null, out value);
        }

        return base.TryReadDefault(target, out value);
    }

    protected internal override bool SameValue(object left, object right) =>
        left is DateTimeOffset first && right is DateTimeOffset second ? first.EqualsExact(second)
        : left is DateTime a && right is DateTime b ? a.Kind == b.Kind && a == b
        : left?.GetType() != right?.GetType() && DateTimeValues.AsMoment(left) == null && DateTimeValues.AsMoment(right) == null
          && DateTimeValues.ToDateTime(left) is { } x && DateTimeValues.ToDateTime(right) is { } y
            ? x == y
            : base.SameValue(left, right);

    protected internal override String TextOf(object value) =>
        DateTimeValues.Text(value, FormatOf(value), CustomFormat, Languages.Culture);

    protected internal override String AutomationTextOf(object value) => value == null ? null : TextOf(value);

    private DateTimeFieldFormat FormatOf(object value) =>
        Format ?? (value != null ? DateTimeValues.FormatFor(value.GetType()) : _built);

    private sealed class Shown(DateTimeProperty definition) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            DateTimeValues.Text(value, definition.FormatOf(value), definition.CustomFormat, culture);

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private static void OnFormatChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not DateTimeProperty definition) return;

        definition._editors.Clear();
        definition._display = null;
        definition.Relayout();
    }
}

/// <summary>A length of time - <see cref="TimeSpan"/> - in a <see cref="DurationField"/>, the parts
/// <see cref="Parts"/> names, between <see cref="Minimum"/> and <see cref="Maximum"/>.</summary>
public class DurationProperty : PropertyDefinition
{
    public static readonly AdamantiumProperty PartsProperty = AdamantiumProperty.Register(nameof(Parts),
        typeof(DurationParts), typeof(DurationProperty),
        new PropertyMetadata(DurationParts.Hours | DurationParts.Minutes | DurationParts.Seconds, OnPartsChanged));

    public static readonly AdamantiumProperty MinimumProperty = AdamantiumProperty.Register(nameof(Minimum),
        typeof(TimeSpan), typeof(DurationProperty), new PropertyMetadata(-TimeSpan.MaxValue));

    public static readonly AdamantiumProperty MaximumProperty = AdamantiumProperty.Register(nameof(Maximum),
        typeof(TimeSpan), typeof(DurationProperty), new PropertyMetadata(TimeSpan.MaxValue));

    /// <summary>The word that follows the days in the line's text, its tip among them; the theme says it in the
    /// application's language.</summary>
    public static readonly AdamantiumProperty DayUnitProperty = AdamantiumProperty.Register(nameof(DayUnit),
        typeof(String), typeof(DurationProperty), new PropertyMetadata(null));

    public String DayUnit
    {
        get => GetValue<String>(DayUnitProperty);
        set => SetValue(DayUnitProperty, value);
    }

    private DataTemplate _editor;
    private DataTemplate _display;

    /// <summary>Which parts are shown and edited, as in <see cref="DurationField.Parts"/>.</summary>
    public DurationParts Parts
    {
        get => GetValue<DurationParts>(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    public TimeSpan Minimum
    {
        get => GetValue<TimeSpan>(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public TimeSpan Maximum
    {
        get => GetValue<TimeSpan>(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected internal override DataTemplate DefaultEditorTemplate =>
        _editor ??= new DataTemplate(() => new TemplateResult { RootComponent = PropertyEditors.Duration() });

    protected internal override DataTemplate DefaultValueTemplate =>
        _display ??= DateTimeValues.DurationTemplate(() => Parts);

    protected internal override void PrepareEditor(IUIComponent editor, object value)
    {
        if (editor is not DurationField field) return;

        field.Parts = Parts;
        field.Minimum = Minimum;
        field.Maximum = Maximum;
        field.Value = value as TimeSpan?;
    }

    protected internal override object ReadEditor(IUIComponent editor)
    {
        var field = editor as DurationField;
        field?.FinishTyping();
        return field?.Value;
    }

    protected internal sealed override bool TryConvert(object edited, Type target, out object value) =>
        TryConvert(edited, target, null, out value);

    protected internal override bool TryConvert(object edited, Type target, object current, out object value)
    {
        if (edited is string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return base.TryConvert(null, target, out value);
            if (!DateTimeValues.TryParseDuration(text, Parts, Languages.Culture, current as TimeSpan?, out var length))
            {
                value = null;
                return false;
            }

            edited = length;
        }

        return base.TryConvert(edited, target, out value);
    }

    protected internal override bool TryReadDefault(Type target, out object value)
    {
        if (DefaultValue is string text && !string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return DateTimeValues.TryParseDuration(text, Parts, System.Globalization.CultureInfo.InvariantCulture, null,
                       out var length) &&
                   base.TryConvert(length, target, out value);
        }

        return base.TryReadDefault(target, out value);
    }

    protected internal override String TextOf(object value)
    {
        return value is TimeSpan length
            ? DateTimeValues.DurationLine(length, Parts, DayUnit, Languages.Culture)
            : base.TextOf(value);
    }

    protected internal override String AutomationTextOf(object value) => value == null ? null : TextOf(value);

    private static void OnPartsChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not DurationProperty definition) return;

        definition._display = null;
        definition.Relayout();
    }
}

/// <summary>An <see cref="ImageSource"/>: shows the file path, accepts a typed one, and "..." opens the system file
/// picker.</summary>
public class ImageSourceProperty : PropertyDefinition
{
    /// <summary>What the file dialog the "..." opens asks. The theme says it in the application's language.</summary>
    public static readonly AdamantiumProperty DialogTitleProperty = AdamantiumProperty.Register(nameof(DialogTitle),
        typeof(String), typeof(ImageSourceProperty), new PropertyMetadata(null));

    /// <summary>What that dialog calls the pictures it offers.</summary>
    public static readonly AdamantiumProperty PicturesNameProperty = AdamantiumProperty.Register(nameof(PicturesName),
        typeof(String), typeof(ImageSourceProperty), new PropertyMetadata(null));

    /// <summary>...and what it calls every other file.</summary>
    public static readonly AdamantiumProperty AllFilesNameProperty = AdamantiumProperty.Register(nameof(AllFilesName),
        typeof(String), typeof(ImageSourceProperty), new PropertyMetadata(null));

    private DataTemplate _editor;

    public ImageSourceProperty()
    {
        // The row's own button, at DEFAULT priority so an application that wants to ask for a file its own way can
        // still bind one over it. A path is something a person points at rather than types.
        SetValue(ShowActionButtonProperty, true, ValuePriority.Default);
        SetValue(ActionCommandProperty, new PickCommand(this), ValuePriority.Default);

        // THE PATH IS ITS OWN TIP: a file two folders deep does not fit the cell, and which file this is is the one
        // question asked of this row.
        SetValue(ValueAsTipProperty, true, ValuePriority.Default);
    }

    public String DialogTitle
    {
        get => GetValue<String>(DialogTitleProperty);
        set => SetValue(DialogTitleProperty, value);
    }

    public String PicturesName
    {
        get => GetValue<String>(PicturesNameProperty);
        set => SetValue(PicturesNameProperty, value);
    }

    public String AllFilesName
    {
        get => GetValue<String>(AllFilesNameProperty);
        set => SetValue(AllFilesNameProperty, value);
    }

    protected internal override DataTemplate DefaultEditorTemplate =>
        _editor ??= new DataTemplate(() => new TemplateResult { RootComponent = PropertyEditors.Field() });

    protected internal override void PrepareEditor(IUIComponent editor, object value)
    {
        if (editor is TextBox box) box.Text = PathOf(value) ?? String.Empty;
    }

    protected internal override object ReadEditor(IUIComponent editor) => (editor as TextBox)?.Text;

    protected internal override String TextOf(object value) => PathOf(value);

    /// <summary>What the line SHOWS is the FILE, so that is what "the same" means here - two objects showing one file
    /// agree as far as this line is concerned, whatever two objects the pictures themselves are.</summary>
    protected internal override bool SameValue(object left, object right) =>
        left is ImageSource || right is ImageSource
            ? PathOf(left) == PathOf(right)
            : base.SameValue(left, right);

    // Nothing is written INTO a picture: a picture is the file it was loaded from, and pointing it at another file is
    // another picture. So the line goes the ordinary way - convert, then write through the binding.
    protected internal override bool TryConvert(object edited, Type target, out object value)
    {
        // EMPTIED ON PURPOSE is an answer: a cleared path means "no picture here", and refusing it would leave a line
        // that can be given a file and never take one away.
        if (edited is String { Length: 0 } or null)
        {
            value = null;
            return true;
        }

        if (Picture(edited as String) is { } source)
        {
            value = source;
            return true;
        }

        return base.TryConvert(edited, target, out value);
    }

    private static ImageSource Picture(String path)
    {
        if (String.IsNullOrWhiteSpace(path)) return null;

        return Uri.TryCreate(path, UriKind.RelativeOrAbsolute, out var uri) ? new BitmapImage(uri) : null;
    }

    private static String PathOf(object picture) =>
        picture is BitmapImage { UriSource: { } uri } ? uri.OriginalString : null;

    // The "..." this row is born with: ask, and write the answer the same way a typed path is written - through the
    // row, so the grid's own before/after report (which is what undo is made of) happens once and in one place.
    private sealed class PickCommand : ICommand
    {
        private readonly ImageSourceProperty _line;

        public PickCommand(ImageSourceProperty line) => _line = line;

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter = null) => FileDialog.IsAvailable;

        public void Execute(object parameter = null)
        {
            if (!FileDialog.IsAvailable) return;

            var path = FileDialog.Open(new OpenFileRequest
            {
                Title = _line.DialogTitle,
                FileTypes =
                [
                    new FileType(_line.PicturesName, "png", "jpg", "jpeg", "bmp", "gif", "tga", "tiff", "ico", "dds"),
                    new FileType(_line.AllFilesName, "*")
                ],

                // ITS OWN MEMORY - the size it was left at and the folder pictures were last taken from, kept apart
                // from every other dialog this application opens - and the window it belongs to, which is what decides
                // the screen it appears on.
                Key = "inspector.picture",
                Owner = Owner(_line)
            });

            if (path == null) return;   // canceled is an answer, and nothing is written

            _line.Pick(path);
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        // THE WINDOW THIS LINE IS SHOWN IN, walked up the tree it stands in: a definition is not an element itself, so
        // the first one above it that is answers. Nothing when it is not in a tree at all - a dialog with no owner
        // still opens, on the main screen.
        private static IntPtr Owner(PropertyDefinition line)
        {
            for (IFundamentalUIComponent at = line; at != null; at = at.LogicalParent)
            {
                if (at is Base.InputUIComponent element) return element.GetWindow()?.Handle ?? IntPtr.Zero;
            }

            return IntPtr.Zero;
        }
    }

    /// <summary>A file was chosen for this line. The GRID listens, so the write goes through the same path a typed one
    /// does - one place that reports the change and one place that is undone.</summary>
    internal event Action<ImageSourceProperty, String> Picked;

    /// <summary>Says a file was chosen for this line - what its own "..." calls, and what an application asking for a
    /// picture its own way calls to answer. The WRITING is the grid's, so an edit made by pointing at a file is the
    /// same edit a typed path is: reported once, and taken back the same way.</summary>
    public void Pick(String path) => Picked?.Invoke(this, path);
}

/// <summary>A property that holds other properties: a vector, a color, a nested object. It has no value of its own -
/// it opens.</summary>
public class CompositeProperty : PropertyDefinition
{
    /// <summary>A short line shown beside the name while it is folded, so the value is readable without opening it:
    /// <c>(12, 0, 4)</c>. Optional.</summary>
    public static readonly AdamantiumProperty SummaryProperty = AdamantiumProperty.Register(nameof(Summary),
        typeof(String), typeof(CompositeProperty), new PropertyMetadata(null));

    public static readonly AdamantiumProperty IsExpandedProperty = AdamantiumProperty.Register(nameof(IsExpanded),
        typeof(bool), typeof(CompositeProperty), new PropertyMetadata(false));

    public String Summary
    {
        get => GetValue<String>(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    /// <summary>Whether the children are shown. Lives on the DEFINITION rather than on the row, so folding survives the
    /// row being rebuilt - and so a view-model can state it.</summary>
    public bool IsExpanded
    {
        get => GetValue<bool>(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }
}

/// <summary>Repeats its <see cref="Children"/> lines for every item of the collection its
/// <see cref="PropertyDefinition.Binding"/> reads; child lines bind against the item.</summary>
public class ItemsProperty : CompositeProperty
{
    public static readonly AdamantiumProperty ItemHeaderProperty = AdamantiumProperty.Register(nameof(ItemHeader),
        typeof(BindingBase), typeof(ItemsProperty), new PropertyMetadata(null));

    public static readonly AdamantiumProperty ItemActionProperty = AdamantiumProperty.Register(nameof(ItemAction),
        typeof(ICommand), typeof(ItemsProperty), new PropertyMetadata(null));

    public static readonly AdamantiumProperty ItemActionIconProperty = AdamantiumProperty.Register(
        nameof(ItemActionIcon), typeof(String), typeof(ItemsProperty), new PropertyMetadata(null));

    public static readonly AdamantiumProperty ItemActionTipProperty = AdamantiumProperty.Register(
        nameof(ItemActionTip), typeof(String), typeof(ItemsProperty), new PropertyMetadata(null));

    /// <summary>What each item is CALLED, read off the item itself - the socket's own name over its two lines. Nothing
    /// numbers them instead, which is what a list whose items have no names of their own wants.</summary>
    public BindingBase ItemHeader
    {
        get => GetValue<BindingBase>(ItemHeaderProperty);
        set => SetValue(ItemHeaderProperty, value);
    }

    /// <summary>What the button beside each item's name does, given that item.
    /// <para>The reason a list needs one: the COUNT can only take things off the end. Asked to drop the second of three
    /// sockets, a count says nothing at all - it drops the third and leaves the second where it was.</para></summary>
    public ICommand ItemAction
    {
        get => GetValue<ICommand>(ItemActionProperty);
        set => SetValue(ItemActionProperty, value);
    }

    /// <summary>The picture on that button and the words it says on hover - see <see cref="PropertyDefinition.ActionIcon"/>.
    /// Stated on the LIST, because the lines the button sits on are made as the grid builds and there is nowhere else to
    /// say it.</summary>
    public String ItemActionIcon
    {
        get => GetValue<String>(ItemActionIconProperty);
        set => SetValue(ItemActionIconProperty, value);
    }

    public String ItemActionTip
    {
        get => GetValue<String>(ItemActionTipProperty);
        set => SetValue(ItemActionTipProperty, value);
    }
}

// The line that carries ONE item's name inside an ItemsProperty. Made by the grid as it builds, and holding nothing
// worth keeping between passes - the item's own lines are the ones that read and write anything.
internal sealed class ItemHeaderLine : PropertyDefinition
{
}

// Every editor in an inspector is the SAME HEIGHT, and that height is the row's: they stretch into it rather than
// standing at whatever their own content asks for. A field as tall as one line of text beside a spinner as tall as its
// buttons beside a list that collapses when it happens to be empty is not a form, it is a pile.
internal static class PropertyEditors
{
    public static TextBox Field() => new()
    {
        BorderThickness = new Thickness(0),
        Padding = new Thickness(4, 0, 4, 0),
        MinWidth = 0,
        MinHeight = 0,
        VerticalAlignment = VerticalAlignment.Stretch,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    // Press and drag sideways to run the value up and down - what a property panel in a 3D tool does, and the fastest
    // way there is to nudge a position or a mass. On by default HERE and off in a form elsewhere, because it takes the
    // press over: dragging inside the field no longer selects text (double-click still does).
    public static NumericUpDown Number() => new()
    {
        MinWidth = 0,
        MinHeight = 0,
        IsDragScrubEnabled = true,
        VerticalAlignment = VerticalAlignment.Stretch,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    public static DropDown Choice() => new()
    {
        MinWidth = 0,
        MinHeight = 0,
        BorderThickness = new Thickness(0),

        // Own padding too: the theme's is cut for a taller drop-down and would push the text below a row-height box.
        Padding = new Thickness(8, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Stretch,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    /// <summary>Segments at their own width, left in the cell: stretched to it they would change size with the column.</summary>
    public static SegmentedControl Segments() => new()
    {
        MinHeight = 0,
        VerticalAlignment = VerticalAlignment.Stretch,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    public static DateTimePickerBase Picker(DateTimePickerBase picker)
    {
        picker.MinWidth = 0;
        picker.MinHeight = 0;
        picker.BorderThickness = new Thickness(0);
        picker.Padding = new Thickness(4, 0, 4, 0);
        picker.VerticalAlignment = VerticalAlignment.Stretch;
        picker.HorizontalAlignment = HorizontalAlignment.Stretch;
        return picker;
    }

    public static DurationField Duration() => new()
    {
        MinWidth = 0,
        MinHeight = 0,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(4, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Stretch,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    // No border of its own: the swatch IS the control, and a frame around a color changes how the color reads.
    public static ColorPickerButton Swatch() => new()
    {
        MinWidth = 0,
        MinHeight = 0,
        VerticalAlignment = VerticalAlignment.Stretch,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    public static DataTemplate Box(bool live) => new(() =>
    {
        var check = new CheckBox
        {
            IsHitTestVisible = live,
            MinWidth = 0,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        var result = new TemplateResult { RootComponent = check };

        // The READ-ONLY box follows the value by binding, because nothing fills it: a property that cannot be edited has
        // no editor to prepare. The live one is filled by the definition itself, and a binding there would fight the
        // write-back.
        if (!live) result.AddBinding(check, "IsChecked", new Core.Data.Binding { Mode = Core.Data.BindingMode.OneWay });

        return result;
    });
}
