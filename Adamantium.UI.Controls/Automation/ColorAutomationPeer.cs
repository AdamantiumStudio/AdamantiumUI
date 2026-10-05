using System;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a control that holds a color - <see cref="ColorPicker"/>, <see cref="ColorWheel"/>: its value is
/// the color as the picker's hex field writes it, <c>#AARRGGBB</c> (<c>#RRGGBB</c> is opaque), and is set the same
/// way.</summary>
public class ColorAutomationPeer : UIComponentAutomationPeer, IValueProvider
{
    private readonly AdamantiumProperty _color;

    public ColorAutomationPeer(Control owner, AdamantiumProperty color) : base(owner)
    {
        _color = color;
    }

    public override AutomationControlType ControlType => AutomationControlType.Custom;

    public string Value => ColorPicker.FormatHex(Owner.GetValue<Color>(_color));

    public bool IsReadOnly => false;

    public void SetValue(string value)
    {
        if (!ColorPicker.TryParseHex(value, out var color))
        {
            throw new FormatException($"'{value}' is not a color: #AARRGGBB or #RRGGBB.");
        }

        Owner.SetCurrentValue(_color, color);
    }
}
