using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Automation;

/// <summary>A finger or a pen as the simulator drives it: which, its id, and what it reports besides its place.</summary>
internal readonly record struct Contact(PointerType Type, uint Id, PointerPointProperties Properties, bool IsPrimary = true,
    float? PressureTo = null)
{
    public Contact Along(double t) => PressureTo is { } to
        ? this with { Properties = Properties with { Pressure = Properties.Pressure + (to - Properties.Pressure) * (float)t } }
        : this;

    public static Contact Finger(int index = 0) =>
        new(PointerType.Touch, 0x100 + (uint)index, new PointerPointProperties { Pressure = PointerPointProperties.DefaultPressure },
            index == 0);

    public static Contact Pen(float pressure, bool barrel, bool eraser, float? pressureTo = null) =>
        new(PointerType.Pen, 0x200, new PointerPointProperties
        {
            Pressure = pressure,
            IsBarrelButtonPressed = barrel,
            IsEraser = eraser
        }, PressureTo: pressureTo);
}

internal readonly record struct ContactPath(UIComponent Element, string Label, Vector2<double> From, Vector2<double>? To);
