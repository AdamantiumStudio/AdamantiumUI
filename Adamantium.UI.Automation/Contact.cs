using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Automation;

/// <summary>A finger or a pen as the simulator drives it: which, its id, and what it reports besides its place.</summary>
internal readonly record struct Contact(PointerType Type, uint Id, PointerPointProperties Properties, bool IsPrimary = true)
{
    public static Contact Finger(int index = 0) =>
        new(PointerType.Touch, 0x100 + (uint)index, new PointerPointProperties { Pressure = PointerPointProperties.DefaultPressure },
            index == 0);

    public static Contact Pen(float pressure, bool barrel, bool eraser) =>
        new(PointerType.Pen, 0x200, new PointerPointProperties
        {
            Pressure = pressure,
            IsBarrelButtonPressed = barrel,
            IsEraser = eraser
        });
}

internal readonly record struct ContactPath(UIComponent Element, string Label, Vector2 From, Vector2? To);
