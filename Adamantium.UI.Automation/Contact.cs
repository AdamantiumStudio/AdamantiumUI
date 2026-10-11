using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Automation;

/// <summary>A finger or a pen as the simulator drives it: which, its id, and what it reports besides its place.</summary>
internal readonly record struct Contact(PointerType Type, uint Id, PointerPointProperties Properties)
{
    public static Contact Finger() =>
        new(PointerType.Touch, 0x100, new PointerPointProperties { Pressure = PointerPointProperties.DefaultPressure });

    public static Contact Pen(float pressure, bool barrel, bool eraser) =>
        new(PointerType.Pen, 0x200, new PointerPointProperties
        {
            Pressure = pressure,
            IsBarrelButtonPressed = barrel,
            IsEraser = eraser
        });
}
