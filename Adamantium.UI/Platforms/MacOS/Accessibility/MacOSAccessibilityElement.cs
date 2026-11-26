using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.UI.Platforms.MacOS.Accessibility;

/// <summary>The <c>NSAccessibilityElement</c> subclass that stands for an automation peer: the methods of the
/// <c>NSAccessibility</c> protocol AppKit asks it, answered by its <see cref="MacOSAccessibilityNode"/>. One whose node is
/// gone - its window closed while an assistive technology still holds it - answers nothing.</summary>
internal static unsafe class MacOSAccessibilityElement
{
    private const string Object = "@@:";
    private const string Boolean = "B@:";

    // NSAccessibilityElement is AppKit's: loaded before the class is made under it, or it is made under nothing.
    private static readonly IntPtr AppKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

    private static readonly IntPtr AutoreleaseSelector = ObjCRuntime.RegisterSelector("autorelease");
    private static readonly IntPtr NumberWithDoubleSelector = ObjCRuntime.RegisterSelector("numberWithDouble:");
    private static readonly IntPtr NumberWithIntegerSelector = ObjCRuntime.RegisterSelector("numberWithInteger:");
    private static readonly IntPtr DoubleValueSelector = ObjCRuntime.RegisterSelector("doubleValue");
    private static readonly IntPtr IsKindOfClassSelector = ObjCRuntime.RegisterSelector("isKindOfClass:");
    private static readonly IntPtr NumberClass = ObjCRuntime.GetClass("NSNumber");
    private static readonly IntPtr StringClass = ObjCRuntime.GetClass("NSString");
    private static readonly IntPtr WindowSelector = ObjCRuntime.RegisterSelector("window");

    private static readonly Dictionary<IntPtr, string> Gated = GatedSelectors();

    // The actions and setters, and the text's getters, offered only where the peer can.
    private static Dictionary<IntPtr, string> GatedSelectors()
    {
        var gated = new Dictionary<IntPtr, string>();
        foreach (var name in (string[])["accessibilityPerformPress", "accessibilityPerformIncrement",
                     "accessibilityPerformDecrement", "setAccessibilityValue:", "setAccessibilityExpanded:",
                     "setAccessibilityFocused:", "accessibilityPerformShowMenu", "setAccessibilitySelectedTextRange:",
                     "isAccessibilityExpanded", "isAccessibilitySelected", "accessibilityMinValue", "accessibilityMaxValue",
                     "accessibilityValue"])
        {
            gated[ObjCRuntime.RegisterSelector(name)] = name;
        }

        foreach (var name in MacOSAccessibilityText.Selectors)
        {
            gated[ObjCRuntime.RegisterSelector(name)] = name;
        }

        return gated;
    }

    /// <summary>The class, registered with the runtime on first use.</summary>
    private static readonly IntPtr Class = new ObjCClassBuilder("AdamantiumAccessibilityElement", "NSAccessibilityElement")
        .AddMethod("accessibilityRole", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Role, Object)
        .AddMethod("accessibilitySubrole", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Subrole, Object)
        .AddMethod("accessibilityLabel", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Label, Object)
        .AddMethod("accessibilityHelp", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Help, Object)
        .AddMethod("accessibilityIdentifier", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Identifier, Object)
        .AddMethod("accessibilityValue", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Value, Object)
        .AddMethod("setAccessibilityValue:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&SetValue, "v@:@")
        .AddMethod("accessibilityMinValue", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Minimum, Object)
        .AddMethod("accessibilityMaxValue", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Maximum, Object)
        .AddMethod("accessibilityFrame", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, CGRect>)&Frame,
            "{CGRect={CGPoint=dd}{CGSize=dd}}@:")
        .AddMethod("accessibilityParent", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Parent, Object)
        .AddMethod("accessibilityChildren", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Children, Object)
        .AddMethod("accessibilityWindow", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Window, Object)
        .AddMethod("accessibilityTopLevelUIElement", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Window, Object)
        .AddMethod("isAccessibilityElement", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&IsElement, Boolean)
        .AddMethod("isAccessibilityEnabled", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&IsEnabled, Boolean)
        .AddMethod("isAccessibilityFocused", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&IsFocused, Boolean)
        .AddMethod("setAccessibilityFocused:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte, void>)&SetFocused, "v@:B")
        .AddMethod("isAccessibilitySelected", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&IsSelected, Boolean)
        .AddMethod("isAccessibilityExpanded", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&IsExpanded, Boolean)
        .AddMethod("setAccessibilityExpanded:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte, void>)&SetExpanded, "v@:B")
        .AddMethod("accessibilityPerformPress", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&Press, Boolean)
        .AddMethod("accessibilityPerformIncrement", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&Increment, Boolean)
        .AddMethod("accessibilityPerformDecrement", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&Decrement, Boolean)
        .AddMethod("accessibilityPerformShowMenu", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&ShowMenu, Boolean)
        .AddMethod("isAccessibilitySelectorAllowed:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)&IsAllowed, "B@::")
        .AddText()
        .Register();

    /// <summary>A new element for <paramref name="node"/>, owned by the caller (<see cref="Release"/>).</summary>
    public static IntPtr Create(MacOSAccessibilityNode node)
    {
        var element = NSObject.Init(NSObject.Alloc(Class));
        ObjCManagedObject.Attach(element, node);
        return element;
    }

    /// <summary>The node of <paramref name="element"/>; null once it is let go of.</summary>
    public static MacOSAccessibilityNode NodeOf(IntPtr element) =>
        element == IntPtr.Zero ? null : ObjCManagedObject.Get<MacOSAccessibilityNode>(element);

    /// <summary>Lets go of <paramref name="element"/>: its node goes, and the caller's reference.</summary>
    public static void Release(IntPtr element)
    {
        ObjCManagedObject.Release(element);
        NSObject.Release(element);
    }

    [UnmanagedCallersOnly]
    private static IntPtr Role(IntPtr self, IntPtr selector) => Text(NodeOf(self)?.Role);

    [UnmanagedCallersOnly]
    private static IntPtr Subrole(IntPtr self, IntPtr selector) => Text(NodeOf(self)?.Subrole);

    [UnmanagedCallersOnly]
    private static IntPtr Label(IntPtr self, IntPtr selector) => Text(NodeOf(self)?.Label);

    [UnmanagedCallersOnly]
    private static IntPtr Help(IntPtr self, IntPtr selector) => Text(NodeOf(self)?.Help);

    [UnmanagedCallersOnly]
    private static IntPtr Identifier(IntPtr self, IntPtr selector) => Text(NodeOf(self)?.Identifier);

    [UnmanagedCallersOnly]
    private static IntPtr Value(IntPtr self, IntPtr selector) => NodeOf(self)?.Value switch
    {
        int number => Integer(number),
        double number => Number(number),
        string text => Text(text),
        _ => IntPtr.Zero
    };

    [UnmanagedCallersOnly]
    private static void SetValue(IntPtr self, IntPtr selector, IntPtr value)
    {
        if (NodeOf(self) is not { } node || value == IntPtr.Zero)
            return;

        if (IsKind(value, NumberClass))
            node.SetNumber(NSObject.SendForDouble(value, DoubleValueSelector));
        else if (IsKind(value, StringClass))
            node.SetText(NSString.ToManaged(value));
    }

    [UnmanagedCallersOnly]
    private static IntPtr Minimum(IntPtr self, IntPtr selector) =>
        NodeOf(self)?.Minimum is { } minimum ? Number(minimum) : IntPtr.Zero;

    [UnmanagedCallersOnly]
    private static IntPtr Maximum(IntPtr self, IntPtr selector) =>
        NodeOf(self)?.Maximum is { } maximum ? Number(maximum) : IntPtr.Zero;

    [UnmanagedCallersOnly]
    private static CGRect Frame(IntPtr self, IntPtr selector) => NodeOf(self)?.Frame ?? default;

    [UnmanagedCallersOnly]
    private static IntPtr Parent(IntPtr self, IntPtr selector) =>
        NodeOf(self) is { Peer: { } peer } node ? node.Owner.ParentOf(peer) : IntPtr.Zero;

    [UnmanagedCallersOnly]
    private static IntPtr Children(IntPtr self, IntPtr selector) =>
        NodeOf(self) is { } node ? node.Owner.ChildrenOf(node) : NSArray.Create([]);

    [UnmanagedCallersOnly]
    private static IntPtr Window(IntPtr self, IntPtr selector) =>
        NodeOf(self)?.Owner.Container is { } view && view != IntPtr.Zero ? NSObject.Send(view, WindowSelector) : IntPtr.Zero;

    [UnmanagedCallersOnly]
    private static byte IsElement(IntPtr self, IntPtr selector) => Flag(NodeOf(self)?.IsElement);

    [UnmanagedCallersOnly]
    private static byte IsEnabled(IntPtr self, IntPtr selector) => Flag(NodeOf(self)?.IsEnabled);

    [UnmanagedCallersOnly]
    private static byte IsFocused(IntPtr self, IntPtr selector) => Flag(NodeOf(self)?.IsFocused);

    [UnmanagedCallersOnly]
    private static void SetFocused(IntPtr self, IntPtr selector, byte focused)
    {
        if (focused != 0)
            NodeOf(self)?.Focus();
    }

    [UnmanagedCallersOnly]
    private static byte IsSelected(IntPtr self, IntPtr selector) => Flag(NodeOf(self)?.IsSelected);

    [UnmanagedCallersOnly]
    private static byte IsExpanded(IntPtr self, IntPtr selector) => Flag(NodeOf(self)?.IsExpanded);

    [UnmanagedCallersOnly]
    private static void SetExpanded(IntPtr self, IntPtr selector, byte expanded) => NodeOf(self)?.SetExpanded(expanded != 0);

    [UnmanagedCallersOnly]
    private static byte Press(IntPtr self, IntPtr selector) => Flag(NodeOf(self)?.Press());

    [UnmanagedCallersOnly]
    private static byte Increment(IntPtr self, IntPtr selector) => Flag(NodeOf(self)?.Step(1));

    [UnmanagedCallersOnly]
    private static byte Decrement(IntPtr self, IntPtr selector) => Flag(NodeOf(self)?.Step(-1));

    [UnmanagedCallersOnly]
    private static byte ShowMenu(IntPtr self, IntPtr selector) => Flag(NodeOf(self)?.ShowMenu());

    // An action or setter the peer has no way to do is not offered; what is not gated, AppKit decides.
    [UnmanagedCallersOnly]
    private static byte IsAllowed(IntPtr self, IntPtr selector, IntPtr asked) =>
        !Gated.TryGetValue(asked, out var name) ? (byte)1 : Flag(NodeOf(self)?.Offers(name));

    private static byte Flag(bool? value) => value == true ? (byte)1 : (byte)0;

    private static bool IsKind(IntPtr value, IntPtr objCClass) =>
        NSObject.SendForBoolean(value, IsKindOfClassSelector, objCClass);

    // What a getter gives is autoreleased: the caller does not own it.
    private static IntPtr Text(string text) =>
        text == null ? IntPtr.Zero : NSObject.Send(NSString.Create(text), AutoreleaseSelector);

    private static IntPtr Number(double value) =>
        ((delegate* unmanaged<IntPtr, IntPtr, double, IntPtr>)ObjCRuntime.MessageSend)(NumberClass, NumberWithDoubleSelector, value);

    private static IntPtr Integer(int value) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nint, IntPtr>)ObjCRuntime.MessageSend)(NumberClass, NumberWithIntegerSelector, value);
}
