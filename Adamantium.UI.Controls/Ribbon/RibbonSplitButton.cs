using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls;

/// <summary>A split command: the body runs <see cref="Primitives.ButtonBase.Command"/>, the arrow drops the menu. The half
/// is decided on press; Space and Enter run the action.</summary>
public class RibbonSplitButton : RibbonDropDownButton
{
    /// <summary>Whether the pointer is over the ARROW half. A split button that highlights as one piece cannot say
    /// which of its two things a click is about to do, so the theme lights the halves separately.</summary>
    public static readonly AdamantiumProperty IsPointerOverDropDownProperty =
        AdamantiumProperty.RegisterReadOnly(nameof(IsPointerOverDropDown),
            typeof(bool), typeof(RibbonSplitButton), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IndicatorBrushProperty = AdamantiumProperty.Register(nameof(IndicatorBrush),
        typeof(Brush), typeof(RibbonSplitButton), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    private InputUIComponent _dropDownArea;
    private bool _pressedDropDown;

    public bool IsPointerOverDropDown
    {
        get => GetValue<bool>(IsPointerOverDropDownProperty);
        private set => SetValue(IsPointerOverDropDownProperty, value);
    }

    /// <summary>A bar under the icon in what the body applies - the font color or the highlight picked last, as in
    /// Word; null draws none.</summary>
    public Brush IndicatorBrush
    {
        get => GetValue<Brush>(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _dropDownArea = GetTemplateChild("PART_DropDownArea") as InputUIComponent;
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        _dropDownArea = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.ChangedButton == MouseButtons.Left)
        {
            _pressedDropDown = IsOverDropDownArea(e);
        }

        base.OnPointerPressed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _pressedDropDown = false;
        base.OnKeyDown(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        IsPointerOverDropDown = IsOverDropDownArea(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        IsPointerOverDropDown = false;
    }

    // Only the arrow half is a toggle. Reached from the base click for a press on the body, where it must do nothing.
    protected override void OnToggle()
    {
        if (_pressedDropDown) base.OnToggle();
    }

    protected override void OnClick()
    {
        if (_pressedDropDown)
        {
            base.OnToggle();
            return;
        }

        base.OnClick();
    }

    private bool IsOverDropDownArea(PointerEventArgs e)
    {
        if (_dropDownArea == null) return false;

        var point = e.GetPosition(_dropDownArea);
        return point.X >= 0 && point.X < _dropDownArea.ActualWidth
            && point.Y >= 0 && point.Y < _dropDownArea.ActualHeight;
    }
}
