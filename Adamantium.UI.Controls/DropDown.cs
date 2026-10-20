using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Adamantium.ProceduralGeometry;
using Adamantium.Core.Commands;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A single-select drop-down: the header shows the selection, a light-dismissing popup lists
/// <see cref="DropDownItem"/>s, and <see cref="EnumType"/> binds an enum directly. <see cref="IsEditable"/> lets a value
/// be typed into the header, as a font or a size is.</summary>
public class DropDown : Selector
{
    public static readonly AdamantiumProperty IsDropDownOpenProperty = AdamantiumProperty.Register(nameof(IsDropDownOpen),
        typeof(bool), typeof(DropDown), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender, OnIsDropDownOpenChanged));

    public static readonly AdamantiumProperty MaxDropDownHeightProperty = AdamantiumProperty.Register(nameof(MaxDropDownHeight),
        typeof(double), typeof(DropDown), new PropertyMetadata(320.0));

    public static readonly AdamantiumProperty PlaceholderProperty = AdamantiumProperty.Register(nameof(Placeholder),
        typeof(object), typeof(DropDown), new PropertyMetadata(null, OnPlaceholderChanged));

    // What the header shows: the selected item, or the placeholder when nothing is selected. Read-only for the template.
    public static readonly AdamantiumProperty DisplayContentProperty = AdamantiumProperty.Register(nameof(DisplayContent),
        typeof(object), typeof(DropDown), new PropertyMetadata(null));

    // Bind ItemsSource to an enum's values with no ceremony: EnumType="{x:Type local:MyEnum}" (or {x:Enum ...}). Selecting
    // a row sets SelectedItem to the enum value; display is the value's ToString (use ItemTemplate for friendly names).
    public static readonly AdamantiumProperty EnumTypeProperty = AdamantiumProperty.Register(nameof(EnumType),
        typeof(Type), typeof(DropDown), new PropertyMetadata(null, OnEnumTypeChanged));

    // Header chrome (like the other controls': set by the theme, TemplateBound by the default template).
    // Header hover fill (projected by the template trigger), like ButtonBase.
    public static readonly AdamantiumProperty BackgroundPointerOverProperty = AdamantiumProperty.Register(
        nameof(BackgroundPointerOver), typeof(Brush), typeof(DropDown), new PropertyMetadata(default(Brush)));

    public static readonly AdamantiumProperty CommandProperty = AdamantiumProperty.Register(nameof(Command),
        typeof(ICommand), typeof(DropDown), new PropertyMetadata(null, OnCommandChanged));

    public static readonly AdamantiumProperty IsEditableProperty = AdamantiumProperty.Register(nameof(IsEditable),
        typeof(bool), typeof(DropDown), new PropertyMetadata(false, OnIsEditableChanged));

    private Popup _popup;
    private TextBox _text;
    private bool _writingText;
    private bool _edited;
    private CommandSourceListener<DropDown> _commandListener;
    private bool _showingAnswer;
    private object _answer;

    static DropDown()
    {
        // A drop-down is a keyboard-focus target (it Focus()es itself on open) - opt in, since the base default is false.
        FocusableProperty.OverrideMetadata(typeof(DropDown), new PropertyMetadata(true));
    }

    /// <summary>Closes the list when focus leaves the drop-down; a click on one of its rows does not count as leaving.</summary>
    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (!IsDropDownOpen) return;
        if (FocusManager.Focused is DropDownItem row && ReferenceEquals(row.Owner, this)) return;
        IsDropDownOpen = false;
    }

    // An editable one's box is what takes the keyboard, as a spin box's does: the drop-down itself is no stop.
    private static void OnIsEditableChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var dropDown = (DropDown)a;
        dropDown.SetCurrentValue(FocusableProperty, !(bool)e.NewValue);
        dropDown.ShowText();
    }

    // The box's own keys: the arrows walk an open list, F4 drops it, Escape gives back what was shown and the keyboard
    // to the field the drop-down acts for. An open list's Escape is the popup's.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || !IsEditable || !IsWithin(e.OriginalSource as IUIComponent, _text)) return;

        switch (e.Key)
        {
            case Key.DownArrow or Key.UpArrow when IsDropDownOpen:
                StepHighlight(e.Key == Key.DownArrow ? 1 : -1);
                break;
            case Key.F4:
                IsDropDownOpen = !IsDropDownOpen;
                break;
            case Key.Escape when !IsDropDownOpen:
                _edited = false;
                ShowText();
                FocusManager.FocusToReturnTo(this)?.Focus();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEnabled)
            return;

        // What is typed into the box is the box's: a space is a space in a font's name.
        if (IsEditable && IsWithin(e.OriginalSource as IUIComponent, _text))
            return;

        if (!IsDropDownOpen)
        {
            // Enter and Space open it - NOT an arrow. In this engine the arrows are how the keyboard moves BETWEEN
            // controls, so a closed drop-down that answered one would both open itself unasked and swallow the key,
            // trapping the walk on it. Opening is a choice; passing over is not.
            if (e.Key is not (Key.Enter or Key.Space))
                return;

            IsDropDownOpen = true;   // opening sets the highlight to the current value (see OnIsDropDownOpenChanged)
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.DownArrow:
            case Key.UpArrow:
                StepHighlight(e.Key == Key.DownArrow ? 1 : -1);
                e.Handled = true;
                break;
            case Key.Enter or Key.Space:
                Commit();
                e.Handled = true;
                break;
            case Key.Escape:
                IsDropDownOpen = false;   // nothing to put back: walking the list never changed the value
                e.Handled = true;
                break;
        }
    }

    /// <summary>The row the arrows are on. -1 = none. Only meaningful while the list is open; committed by Enter.</summary>
    private int _highlightedIndex = -1;

    // One row along, clamped at the ends - the same step the wheel takes over a CLOSED drop-down, except that there the
    // step IS the choice (there is no list to walk and nothing to commit later).
    private void StepHighlight(int delta)
    {
        var count = Items.Count;
        if (count == 0) return;

        var from = _highlightedIndex < 0 ? (delta > 0 ? -1 : count) : _highlightedIndex;
        Highlight(Math.Clamp(from + delta, 0, count - 1));
    }

    private void Highlight(int index)
    {
        if (_highlightedIndex == index) return;
        SetHighlight(_highlightedIndex, false);
        _highlightedIndex = index;
        SetHighlight(_highlightedIndex, true);
    }

    private void SetHighlight(int index, bool highlighted)
    {
        if (index >= 0 && ItemContainerGenerator.ContainerFromIndex(index) is DropDownItem row)
            row.IsHighlighted = highlighted;
    }

    // Enter takes the highlighted row - which is the ONLY moment the value changes, so everything bound to it hears
    // about the choice once, not once per arrow key.
    private void Commit()
    {
        if (_highlightedIndex >= 0 && _highlightedIndex != SelectedIndex) SelectSingle(_highlightedIndex);
        IsDropDownOpen = false;
    }

    public DropDown()
    {
        SelectionChanged += (_, _) =>
        {
            UpdateDisplayContent();
            RunCommand();
        };
        PointerWheelChanged += OnHeaderWheel;
    }

    // Wheel over the CLOSED drop-down steps the selection: down = next item, up = previous. Off unless asked for - see
    // StepsWithMouseWheel. When the list is open we leave the event alone so the popup can scroll instead.
    private void OnHeaderWheel(object sender, PointerWheelEventArgs e)
    {
        if (!StepsWithMouseWheel || !IsEnabled || IsDropDownOpen) return;
        var count = Items.Count;
        if (count == 0) return;
        var from = SelectedIndex < 0 ? (e.Delta < 0 ? -1 : count) : SelectedIndex;   // start at an edge when unset
        var next = Math.Clamp(from + (e.Delta < 0 ? 1 : -1), 0, count - 1);
        if (next == SelectedIndex) return;   // already at that end of the list: leave the wheel unhandled so the page
                                             // under the cursor keeps scrolling - the same chaining rule ScrollViewer
                                             // follows at its own edge. Swallowing it here dead-ended the wheel on
                                             // every drop-down it passed over.
        SelectedIndex = next;
        e.Handled = true;
    }

    /// <summary>Whether the wheel over the closed list steps the selection; off by default, so scrolling a settings panel does
    /// not rewrite every drop-down it passes.</summary>
    public static readonly AdamantiumProperty StepsWithMouseWheelProperty = AdamantiumProperty.Register(
        nameof(StepsWithMouseWheel), typeof(bool), typeof(DropDown), new PropertyMetadata(false));

    public bool StepsWithMouseWheel
    {
        get => GetValue<bool>(StepsWithMouseWheelProperty);
        set => SetValue(StepsWithMouseWheelProperty, value);
    }

    /// <summary>Whether the popup list is open. Toggled by clicking the header; set false on pick or outside click.</summary>
    public bool IsDropDownOpen
    {
        get => GetValue<bool>(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>Cap on the popup list height; a longer list scrolls. Default 320.</summary>
    public double MaxDropDownHeight
    {
        get => GetValue<double>(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>Shown in the header when nothing is selected (a hint like "Select...").</summary>
    public object Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>What the header presents: <see cref="Selector.SelectedItem"/>, else <see cref="Placeholder"/>. Read-only.</summary>
    public object DisplayContent
    {
        get => GetValue(DisplayContentProperty);
        private set => SetValue(DisplayContentProperty, value);
    }

    /// <summary>Set to an enum type to auto-fill the list with its values (SelectedItem becomes the chosen enum value).</summary>
    public Type EnumType
    {
        get => GetValue<Type>(EnumTypeProperty);
        set => SetValue(EnumTypeProperty, value);
    }

    public Brush BackgroundPointerOver
    {
        get => GetValue<Brush>(BackgroundPointerOverProperty);
        set => SetValue(BackgroundPointerOverProperty, value);
    }

    /// <summary>What a choice runs, given the chosen item's value, as a font box sets the font of the text being edited.
    /// The value a routed command answers with is shown as the choice - one no item has shows in the header as it
    /// is, none shows <see cref="Placeholder"/> - without running it; <see cref="Selector.SelectedValue"/> then follows
    /// the command, not a binding. While the command cannot run, the drop-down is disabled.</summary>
    public ICommand Command
    {
        get => GetValue<ICommand>(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>Whether a value can be typed into the header, as Word's font and size boxes take one. Enter takes it: the
    /// item that reads the same, ignoring case, or - with a <see cref="Command"/> - the text as a value of the items'
    /// type, run as a pick is ("13" for a size no item has). Escape, or leaving the box, gives back what was shown.</summary>
    public bool IsEditable
    {
        get => GetValue<bool>(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    private static void OnCommandChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var dropDown = (DropDown)a;
        dropDown._commandListener ??= new CommandSourceListener<DropDown>(dropDown, static d => d.UpdateCommandState());
        dropDown._commandListener.Listen(e.NewValue as ICommand);
        dropDown.UpdateCommandState();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _commandListener?.Attached();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _commandListener?.Detached();
    }

    private void UpdateCommandState()
    {
        var state = CommandManager.QueryFrom(Command, null, this, null);
        SetCurrentValue(IsEnabledProperty, state.CanExecute);
        if (Command is not RoutedCommand)
        {
            return;
        }

        _showingAnswer = true;
        try
        {
            SetCurrentValue(SelectedValueProperty, state.Value);
        }
        finally
        {
            _showingAnswer = false;
        }

        _answer = SelectedItem == null ? state.Value : null;
        UpdateDisplayContent();
    }

    private void RunCommand()
    {
        if (!_showingAnswer && SelectedItem != null && Command is { } command)
        {
            _answer = null;
            CommandManager.ExecuteFrom(command, SelectedValue, this, null);
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // The list card lives in the popup's lazily-built ChildTemplate, so base.OnApplyTemplate cannot find
        // PART_ItemsPresenter yet - it is connected when the content arrives on first open.
        _popup = GetTemplateChild("PART_Popup") as Popup;
        if (_popup != null)
        {
            _popup.PlacementTarget = this;              // position against the control (== the header)
            _popup.KeepOpen = false;                    // click-outside-to-close, owned by Popup now
            _popup.IgnoreTargetPress = true;            // a header press is handled by us (toggle) - don't dismiss+reopen
            _popup.DataContext = this;                  // the card binds our ActualWidth / MaxDropDownHeight through it
            _popup.Closed -= OnPopupClosed;
            _popup.Closed += OnPopupClosed;
            _popup.ContentBuilt -= OnPopupContentBuilt;
            _popup.ContentBuilt += OnPopupContentBuilt;
            _popup.IsOpen = IsDropDownOpen;
        }

        DetachText();
        _text = GetTemplateChild("PART_EditableTextBox") as TextBox;
        if (_text != null)
        {
            _text.EnterPressed += OnTextEnterPressed;
            _text.LostFocus += OnTextLostFocus;
            _text.PropertyChanged += OnTextChanged;
        }

        UpdateDisplayContent();
    }

    /// <summary>Let the template's parts go when the template does - see ScrollBar.OnRemoveTemplate.</summary>
    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        if (_popup != null)
        {
            _popup.Closed -= OnPopupClosed;
            _popup.ContentBuilt -= OnPopupContentBuilt;
        }
        _popup = null;
        DetachText();
    }

    private void DetachText()
    {
        if (_text == null) return;
        _text.EnterPressed -= OnTextEnterPressed;
        _text.LostFocus -= OnTextLostFocus;
        _text.PropertyChanged -= OnTextChanged;
        _text = null;
    }

    // The box shows what the header would - the choice, or the value the command answers - unless something is being
    // typed into it.
    private void ShowText()
    {
        if (!IsEditable || _text == null || _edited) return;

        _text.Placeholder = Placeholder?.ToString();
        var shown = SelectedItem != null ? FormatForDisplay(SelectedItem) : _answer;
        var text = Convert.ToString(shown, Languages.Culture) ?? string.Empty;
        if (_text.Text == text) return;

        _writingText = true;
        try
        {
            _text.Text = text;
        }
        finally
        {
            _writingText = false;
        }
    }

    // Typing in an open list moves its highlight to the first row that starts so, and off every row when none does -
    // Enter then takes what is typed, not a row passed on the way.
    private void OnTextChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty || _writingText) return;

        _edited = true;
        if (IsDropDownOpen) Highlight(IndexOf(_text.Text, prefix: true));
    }

    private void OnTextEnterPressed(object sender, KeyEventArgs e)
    {
        if (IsDropDownOpen && _highlightedIndex >= 0)
        {
            _edited = false;
            Commit();
            ShowText();
            return;
        }

        IsDropDownOpen = false;
        CommitText();
    }

    // Leaving the box gives back what was shown, and puts the list away unless one of its rows took the keyboard.
    private void OnTextLostFocus(object sender, RoutedEventArgs e)
    {
        _edited = false;
        ShowText();
        if (IsDropDownOpen && !(FocusManager.Focused is DropDownItem row && ReferenceEquals(row.Owner, this)))
        {
            IsDropDownOpen = false;
        }
    }

    internal string TypedText => _text?.Text;

    // As if typed and Enter pressed.
    internal void TakeText(string text)
    {
        if (_text == null || !IsEnabled) return;

        _text.Text = text ?? string.Empty;
        CommitText();
    }

    private void CommitText()
    {
        var typed = _text.Text.Trim();
        _edited = false;
        var index = typed.Length == 0 ? -1 : IndexOf(typed, prefix: false);
        if (index >= 0 && index != SelectedIndex)
        {
            SelectSingle(index);
        }
        else if (index < 0 && typed.Length > 0 && Command is { } command && ValueOfText(typed) is { } value)
        {
            _answer = null;
            CommandManager.ExecuteFrom(command, value, this, null);
        }
        else
        {
            FocusManager.FocusToReturnTo(this)?.Focus();
        }

        ShowText();
    }

    // The row that reads as the text, ignoring case, or holds the value it parses to; with prefix, the first that
    // starts so.
    private int IndexOf(string text, bool prefix)
    {
        if (text.Length == 0) return -1;

        var value = prefix ? null : ValueOfText(text);
        for (var i = 0; i < Items.Count; i++)
        {
            var shown = Convert.ToString(FormatForDisplay(Items[i]), Languages.Culture) ?? string.Empty;
            if (prefix ? shown.StartsWith(text, StringComparison.CurrentCultureIgnoreCase)
                    : shown.Equals(text, StringComparison.CurrentCultureIgnoreCase) ||
                      (value != null && Equals(value, ValueOf(Items[i]))))
            {
                return i;
            }
        }

        return -1;
    }

    // The text as a value of the type the items' values are - a size's number, a font's name; null when it is none.
    private object ValueOfText(string text)
    {
        var type = Items.Count > 0 ? ValueOf(Items[0])?.GetType() : null;
        if (type == null || type == typeof(string)) return text;

        var converter = TypeDescriptor.GetConverter(type);
        if (!converter.CanConvertFrom(typeof(string))) return null;

        try
        {
            return converter.ConvertFromString(null, Languages.Culture, text);
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsWithin(IUIComponent node, IUIComponent ancestor)
    {
        if (ancestor == null) return false;
        for (var n = node; n != null; n = n.VisualParent)
            if (ReferenceEquals(n, ancestor)) return true;
        return false;
    }

    // The items host only exists once the popup has built its deferred content - connect it then.
    private void OnPopupContentBuilt(object sender, EventArgs e)
    {
        if (((Popup)sender).FindContentChild("PART_ItemsPresenter") is ItemsPresenter presenter)
        {
            ConnectPresenter(presenter);
        }
    }

    // Clicking the header toggles the list. The popup's contents live in the overlay layer (not our visual subtree), so a
    // click on an item is hit-tested there and never reaches this handler - only header clicks do.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        // A press in an editable one's box puts the caret there; the list drops from its arrow.
        if (IsEditable && IsWithin(e.OriginalSource as IUIComponent, _text))
        {
            return;
        }

        e.Handled = true;
        if (!IsEditable)
        {
            Focus();
        }
        else if (!ReferenceEquals(FocusManager.Focused, _text))
        {
            // Into the box, unless the keyboard is there already - with what is typed in it.
            _text?.Focus();
        }

        IsDropDownOpen = !IsDropDownOpen;
    }

    // One highlight shared by mouse and keyboard: the pointer moving onto a row moves it, so Enter commits that row.
    internal void HighlightFromContainer(DropDownItem container)
    {
        var index = ItemContainerGenerator.IndexFromContainer(container);
        if (index >= 0) Highlight(index);
    }

    // A row was clicked: select its item and dismiss.
    internal void SelectFromContainer(DropDownItem container)
    {
        var index = ItemContainerGenerator.IndexFromContainer(container);
        if (index >= 0) SelectSingle(index);
        IsDropDownOpen = false;
    }

    // If a template (or selector) is applied AFTER the selection was formatted (property order in markup is not
    // guaranteed), DisplayContent could hold a stale friendly-string that the template would then try to bind against.
    // Re-format the header whenever the item template or its selector changes.
    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ItemTemplateProperty || e.Property == ItemTemplateSelectorProperty)
            UpdateDisplayContent();
    }

    private DropDownItem _shown;

    private void UpdateDisplayContent()
    {
        // The header holds what the picked row HOLDS, read once - so a row whose words change, the language switched
        // under it, has to be read again.
        var shown = SelectedItem as DropDownItem;
        if (!ReferenceEquals(shown, _shown))
        {
            if (_shown != null) _shown.PropertyChanged -= OnShownChanged;
            _shown = shown;
            if (_shown != null) _shown.PropertyChanged += OnShownChanged;
        }

        DisplayContent = SelectedItem != null ? FormatForDisplay(SelectedItem) : _answer ?? Placeholder;
        ShowText();
    }

    private void OnShownChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == ContentControl.ContentProperty) UpdateDisplayContent();
    }

    // What actually shows for an item. With a user ItemTemplate the raw item flows through (the template formats it). With
    // no template, an ENUM value shows its friendly name from [Display(Name)] / [Description] - the thing WPF ignored - so
    // an enum-bound dropdown reads well out of the box while SelectedItem stays the real enum value. Anything else ToStrings.
    private object FormatForDisplay(object item)
    {
        // An item authored as its own container is a CONTROL, and a control can only be in one place: handing it to the
        // header takes the row OUT of the list. Measured while arrowing through an open list - the highlighted row
        // vanished for a frame and the header jumped to its size. Show what the row holds, not the row itself.
        if (item is DropDownItem container) item = container.Content;

        // A template (fixed OR selector-chosen) renders the raw item itself, so pass it through untouched - flattening an
        // enum to its friendly string here would hand the template/selector a string instead of the real value.
        if (ItemTemplate != null || ItemTemplateSelector != null || item is not Enum e) return item;
        var field = e.GetType().GetField(e.ToString());
        if (field != null)
        {
            if (field.GetCustomAttribute<DisplayAttribute>()?.GetName() is { } displayName) return displayName;
            if (field.GetCustomAttribute<DescriptionAttribute>()?.Description is { } description) return description;
        }
        return e.ToString();
    }

    private static void OnPlaceholderChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
        => ((DropDown)a).UpdateDisplayContent();

    private static void OnEnumTypeChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is DropDown dd && e.NewValue is Type { IsEnum: true } enumType)
            dd.ItemsSource = Enum.GetValues(enumType);
    }

    private static void OnIsDropDownOpenChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var dd = (DropDown)a;
        var open = (bool)e.NewValue;
        if (dd._popup != null) dd._popup.IsOpen = open;

        // The list always opens on the current value and closes with no highlight left behind - one place for it,
        // whether it was opened by the keyboard, by a click on the header, or set from code.
        // Opened over something typed, on the first row that starts so.
        if (open) dd.Highlight(dd._edited ? dd.IndexOf(dd._text.Text, prefix: true) : dd.SelectedIndex);
        else dd.Highlight(-1);
        AutomationEvents.RaiseExpandCollapse(dd, open);
    }

    // The popup light-dismissed (a click outside the control + list) - reflect it so the next header click reopens.
    private void OnPopupClosed(object sender, EventArgs e) => IsDropDownOpen = false;

    // --- Container seam: items host as DropDownItem in the popup list ----------------------------------------------

    protected internal override bool IsItemItsOwnContainer(object item) => item is DropDownItem;

    protected internal override IUIComponent GetContainerForItem(object item)
    {
        var container = new DropDownItem { Owner = this };   // back-ref: the popup detaches the container's visual tree
        
        if (ItemContainerStyle != null)
            container.Styles.Add(ItemContainerStyle);
        
        return container;
    }

    protected internal override void PrepareContainer(IUIComponent container, object item)
    {
        if (container is DropDownItem row && !ReferenceEquals(row, item))
        {
            row.DataContext = item;
            row.ContentTemplate = ItemTemplate;
            row.ContentTemplateSelector = ItemTemplateSelector;
            row.Content = FormatForDisplay(item);   // friendly enum name when no ItemTemplate; raw item otherwise
            ApplyContainerSelection(row, item);
            // Rows are made when the popup opens, i.e. AFTER the highlight was set - so a container asks for its own
            // state on arrival rather than the highlight reaching for containers that do not exist yet.
            row.IsHighlighted = Items.IndexOf(item) == _highlightedIndex;
        }
    }

    protected internal override void ClearContainer(IUIComponent container)
    {
        if (container is DropDownItem row)
        {
            row.DataContext = null;
            row.IsSelected = false;
            row.IsHighlighted = false;   // containers are recycled: a stale highlight would follow one into another row
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DropDownAutomationPeer(this);
}
