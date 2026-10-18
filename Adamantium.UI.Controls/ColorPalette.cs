using System.Collections.Generic;
using Adamantium.Core.Commands;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>Colors to pick from, as Word's font color menu holds them: a choice of no color, a grid of
/// <see cref="Colors"/> and, under "More colors", a <see cref="ColorPicker"/> whose color "Apply" takes. A pick runs
/// <see cref="Command"/> with the color - null for no color - keeps it as <see cref="SelectedColor"/>, for a split
/// button's body to apply again, and closes the menu the palette stands in.</summary>
public class ColorPalette : Control
{
    /// <summary>Word's standard colors under a row of grays.</summary>
    public static IReadOnlyList<Color> StandardColors { get; } =
    [
        Color.FromRgba(0, 0, 0, 255), Color.FromRgba(64, 64, 64, 255), Color.FromRgba(89, 89, 89, 255),
        Color.FromRgba(127, 127, 127, 255), Color.FromRgba(166, 166, 166, 255), Color.FromRgba(191, 191, 191, 255),
        Color.FromRgba(217, 217, 217, 255), Color.FromRgba(242, 242, 242, 255), Color.FromRgba(255, 255, 255, 255),
        Color.FromRgba(68, 84, 106, 255),
        Color.FromRgba(192, 0, 0, 255), Color.FromRgba(255, 0, 0, 255), Color.FromRgba(255, 192, 0, 255),
        Color.FromRgba(255, 255, 0, 255), Color.FromRgba(146, 208, 80, 255), Color.FromRgba(0, 176, 80, 255),
        Color.FromRgba(0, 176, 240, 255), Color.FromRgba(0, 112, 192, 255), Color.FromRgba(0, 32, 96, 255),
        Color.FromRgba(112, 48, 160, 255),
    ];

    public static readonly AdamantiumProperty ColorsProperty = AdamantiumProperty.Register(nameof(Colors),
        typeof(IReadOnlyList<Color>), typeof(ColorPalette), new PropertyMetadata(StandardColors));

    public static readonly AdamantiumProperty CommandProperty = AdamantiumProperty.Register(nameof(Command),
        typeof(ICommand), typeof(ColorPalette), new PropertyMetadata(null, OnCommandChanged));

    public static readonly AdamantiumProperty SelectedColorProperty = AdamantiumProperty.Register(nameof(SelectedColor),
        typeof(Color?), typeof(ColorPalette), new PropertyMetadata(null, PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty NoColorTextProperty = AdamantiumProperty.Register(nameof(NoColorText),
        typeof(object), typeof(ColorPalette), new PropertyMetadata(null, (a, _) => ((ColorPalette)a).ShowNoColor()));

    public static readonly AdamantiumProperty IsMoreColorsOpenProperty = AdamantiumProperty.Register(
        nameof(IsMoreColorsOpen), typeof(bool), typeof(ColorPalette), new PropertyMetadata(false));

    private CommandSourceListener<ColorPalette> _commandListener;
    private ButtonBase _noColor;
    private ButtonBase _moreColors;
    private ButtonBase _apply;
    private ColorPicker _picker;

    /// <summary>The colors of the grid; <see cref="StandardColors"/> unless set.</summary>
    public IReadOnlyList<Color> Colors
    {
        get => GetValue<IReadOnlyList<Color>>(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    /// <summary>What a pick runs, given the color, or null for no color; the palette is disabled while it cannot run.</summary>
    public ICommand Command
    {
        get => GetValue<ICommand>(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>The color picked last; null when no color was.</summary>
    public Color? SelectedColor
    {
        get => GetValue<Color?>(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>The words of the choice of no color - "Automatic" for text, "No color" for a highlighter; null leaves
    /// the choice out.</summary>
    public object NoColorText
    {
        get => GetValue(NoColorTextProperty);
        set => SetValue(NoColorTextProperty, value);
    }

    /// <summary>Whether the <see cref="ColorPicker"/> under "More colors" is shown.</summary>
    public bool IsMoreColorsOpen
    {
        get => GetValue<bool>(IsMoreColorsOpenProperty);
        set => SetValue(IsMoreColorsOpenProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        DetachParts();
        _noColor = GetTemplateChild("PART_NoColor") as ButtonBase;
        _moreColors = GetTemplateChild("PART_MoreColors") as ButtonBase;
        _apply = GetTemplateChild("PART_Apply") as ButtonBase;
        _picker = GetTemplateChild("PART_Picker") as ColorPicker;
        if (_noColor != null) _noColor.Click += OnNoColorClick;
        if (_moreColors != null) _moreColors.Click += OnMoreColorsClick;
        if (_apply != null) _apply.Click += OnApplyClick;
        ShowNoColor();
    }

    /// <summary>Let the template's parts go when the template does - see ScrollBar.OnRemoveTemplate.</summary>
    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        DetachParts();
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

    protected override AutomationPeer OnCreateAutomationPeer() => new GroupAutomationPeer(this);

    internal void Pick(Color? color)
    {
        SetCurrentValue(SelectedColorProperty, color);
        SetCurrentValue(IsMoreColorsOpenProperty, false);
        CommandManager.ExecuteFrom(Command, color, this, null);
        RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, this) { RoutedEvent = MenuItem.ClickEvent });
    }

    private static void OnCommandChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var palette = (ColorPalette)a;
        palette._commandListener ??= new CommandSourceListener<ColorPalette>(palette, static p => p.UpdateCanExecute());
        palette._commandListener.Listen(e.NewValue as ICommand);
        palette.UpdateCanExecute();
    }

    private void UpdateCanExecute() =>
        SetCurrentValue(IsEnabledProperty, CommandManager.QueryFrom(Command, null, this, null).CanExecute);

    private void ShowNoColor()
    {
        if (_noColor is UIComponent noColor)
        {
            noColor.Visibility = NoColorText == null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void OnNoColorClick(object sender, RoutedEventArgs e) => Pick(null);

    private void OnMoreColorsClick(object sender, RoutedEventArgs e)
    {
        if (!IsMoreColorsOpen && _picker != null)
        {
            _picker.SelectedColor = SelectedColor ?? Color.FromRgba(0, 0, 0, 255);
        }

        SetCurrentValue(IsMoreColorsOpenProperty, !IsMoreColorsOpen);
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (_picker != null)
        {
            Pick(_picker.SelectedColor);
        }
    }

    private void DetachParts()
    {
        if (_noColor != null) _noColor.Click -= OnNoColorClick;
        if (_moreColors != null) _moreColors.Click -= OnMoreColorsClick;
        if (_apply != null) _apply.Click -= OnApplyClick;
        _noColor = null;
        _moreColors = null;
        _apply = null;
        _picker = null;
    }
}
