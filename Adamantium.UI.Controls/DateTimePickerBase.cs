using System;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A <see cref="DateTimeField"/> with a button that opens a choice beside it - a calendar, the times of a day or
/// both: what the field holds is entered part by part or picked there. Alt+Down and F4 open the choice; Escape, F4 and
/// Alt+Up close it.</summary>
public abstract class DateTimePickerBase : DateTimeField
{
    public static readonly AdamantiumProperty IsDropDownOpenProperty = AdamantiumProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(DateTimePickerBase),
        new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault, OnIsDropDownOpenChanged));

    public static readonly AdamantiumProperty ButtonIconProperty = AdamantiumProperty.Register(nameof(ButtonIcon),
        typeof(ImageSource), typeof(DateTimePickerBase), new PropertyMetadata(null));

    private const InputModifiers Alt = InputModifiers.LeftAlt | InputModifiers.RightAlt;

    private ButtonBase _button;
    private Popup _popup;
    private Border _flyout;
    private bool _openedByKeyboard;
    private bool _picking;
    private int _revealsLeft;

    /// <summary>Whether the choice is open. Two-way by default.</summary>
    public bool IsDropDownOpen
    {
        get => GetValue<bool>(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>The picture on the button - set by the theme for each picker.</summary>
    public ImageSource ButtonIcon
    {
        get => GetValue<ImageSource>(ButtonIconProperty);
        set => SetValue(ButtonIconProperty, value);
    }

    /// <summary>What the button opens, once it has been opened: the calendar and the times inside it.</summary>
    public UIComponent DropDownContent => _flyout?.Child as UIComponent;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _button = GetTemplateChild("PART_Button") as ButtonBase;
        _popup = GetTemplateChild("PART_Popup") as Popup;
        if (_button != null)
        {
            _button.Click += OnButtonClick;
        }

        if (_popup != null)
        {
            _popup.PlacementTarget = this;
            _popup.KeepOpen = false;
            _popup.IgnoreTargetPress = true;
            _popup.Closed += OnPopupClosed;
            if (IsDropDownOpen)
            {
                Open();
            }
        }
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        if (_button != null)
        {
            _button.Click -= OnButtonClick;
        }

        if (_popup != null)
        {
            _popup.Closed -= OnPopupClosed;
            _popup.LayerPass -= OnLayerPass;
            _popup.Child = null;
        }

        _button = null;
        _popup = null;
    }

    /// <summary>Builds what the button opens; called once, on the first opening.</summary>
    protected abstract MeasurableUIComponent CreateDropDownContent();

    /// <summary>Shows the value in what the button opens, each time it opens.</summary>
    protected abstract void PrepareDropDownContent();

    /// <summary>The part of what the button opens the keyboard goes to.</summary>
    protected abstract IInputComponent DropDownKeyboardTarget();

    /// <summary>What the button opens has been laid out and has the keyboard: what has to be scrolled to can be.</summary>
    protected virtual void OnDropDownShown()
    {
    }

    /// <summary>What the button opens has closed: a drag inside it lets the pointer go.</summary>
    protected virtual void OnDropDownClosed()
    {
    }

    /// <summary>Takes what was picked; <paramref name="close"/> closes the choice and gives the keyboard back to the
    /// field.</summary>
    protected void Pick(DateTime value, bool close)
    {
        _picking = true;
        try
        {
            SetCurrentValue(ValueProperty, value);
        }
        finally
        {
            _picking = false;
        }

        if (close)
        {
            CloseDropDown();
        }
    }

    /// <summary>Drops what the button opens, to be built afresh on the next opening.</summary>
    protected void ForgetDropDownContent()
    {
        SetCurrentValue(IsDropDownOpenProperty, false);
        if (_popup != null)
        {
            _popup.Child = null;
        }

        _flyout = null;
    }

    /// <summary>Closes the choice and gives the keyboard back to the field.</summary>
    protected void CloseDropDown()
    {
        SetCurrentValue(IsDropDownOpenProperty, false);
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEnabled)
        {
            return;
        }

        if (e.Key == Key.F4 || (e.Key == Key.DownArrow && (e.Modifiers & Alt) != 0))
        {
            _openedByKeyboard = true;
            SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
            e.Handled = true;
        }
    }

    protected override void OnPreviewPointerPressed(PointerPressedEventArgs e)
    {
        if (IsDropDownOpen && !IsOnButton(e))
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
        }

        if (!IsOnButton(e))
        {
            base.OnPreviewPointerPressed(e);
        }
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if ((e.Property == ValueProperty && !_picking) || e.Property == LanguageProperty ||
            e.Property == CalendarSystemProperty || e.Property == FormatProperty)
        {
            RefreshDropDown();
        }
    }

    /// <summary>Shows again what the button opens, while it is open - for a property it shows that has changed.</summary>
    protected void RefreshDropDown()
    {
        if (IsDropDownOpen && _flyout != null)
        {
            PrepareDropDownContent();
        }
    }

    private static void OnIsDropDownOpenChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var picker = (DateTimePickerBase)a;
        if (picker._popup == null)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            picker.Open();
        }
        else
        {
            picker._popup.LayerPass -= picker.OnLayerPass;
            picker._popup.IsOpen = false;
            picker.OnDropDownClosed();
        }
    }

    private void Open()
    {
        if (!Ready())
        {
            return;
        }

        PrepareDropDownContent();
        _revealsLeft = 0;
        _popup.IsOpen = true;
        _popup.LayerPass -= OnLayerPass;
        _popup.LayerPass += OnLayerPass;
    }

    private bool IsOnButton(PointerEventArgs e)
    {
        for (var node = e.OriginalSource as IUIComponent; node != null; node = node.VisualParent)
        {
            if (ReferenceEquals(node, _button))
            {
                return true;
            }
        }

        return false;
    }

    private bool Ready()
    {
        if (_popup == null)
        {
            return false;
        }

        if (_flyout == null)
        {
            _flyout = new Border { Classes = Classes.Parse("DateTimePickerDropDown"), Child = CreateDropDownContent() };
            _flyout.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnDropDownKey));
        }

        if (_popup.Child == null)
        {
            _popup.Child = _flyout;
        }

        return true;
    }

    private void OnLayerPass(object sender, EventArgs e)
    {
        if (_popup == null || DropDownKeyboardTarget() is not { } target)
        {
            return;
        }

        if (_revealsLeft > 0)
        {
            OnDropDownShown();
            if (--_revealsLeft == 0)
            {
                _popup.LayerPass -= OnLayerPass;
            }

            return;
        }

        var method = _openedByKeyboard || FocusManager.IsFocusVisible ? NavigationMethod.Tab : NavigationMethod.Unspecified;
        if (FocusManager.Focus(target, method))
        {
            _openedByKeyboard = false;
            OnDropDownShown();
            _revealsLeft = 1;
        }
    }

    private void OnDropDownKey(object sender, KeyEventArgs e)
    {
        if (!e.Handled && (e.Key == Key.Escape || e.Key == Key.F4 || (e.Key == Key.UpArrow && (e.Modifiers & Alt) != 0)))
        {
            CloseDropDown();
            e.Handled = true;
        }
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        _openedByKeyboard = false;
        if (!IsDropDownOpen)
        {
            Focus();
        }

        SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
    }

    private void OnPopupClosed(object sender, EventArgs e)
    {
        _popup.LayerPass -= OnLayerPass;
        SetCurrentValue(IsDropDownOpenProperty, false);
    }
}
