using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>What the input method asks of the view a window's content is (NSTextInputClient). Main thread.</summary>
internal static unsafe class MacOSTextInputMethods
{
    private const string Range = "{_NSRange=QQ}";
    private const string Rectangle = "{CGRect={CGPoint=dd}{CGSize=dd}}";

    internal static ObjCClassBuilder AddTextInput(this ObjCClassBuilder view) => view
        .AddMethod("insertText:replacementRange:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, NSRange, void>)&Insert, $"v@:@{Range}")
        .AddMethod("setMarkedText:selectedRange:replacementRange:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, NSRange, NSRange, void>)&Compose, $"v@:@{Range}{Range}")
        .AddMethod("unmarkText", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&Commit, "v@:")
        .AddMethod("doCommandBySelector:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Command, "v@::")
        .AddMethod("hasMarkedText", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&IsComposing, "B@:")
        .AddMethod("markedRange", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange>)&MarkedRange, $"{Range}@:")
        .AddMethod("selectedRange", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange>)&SelectedRange, $"{Range}@:")
        .AddMethod("attributedSubstringForProposedRange:actualRange:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange, NSRange*, IntPtr>)&Part, $"@@:{Range}^{Range}")
        .AddMethod("validAttributesForMarkedText", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Attributes, "@@:")
        .AddMethod("firstRectForCharacterRange:actualRange:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange, NSRange*, CGRect>)&Caret, $"{Rectangle}@:{Range}^{Range}")
        .AddMethod("characterIndexForPoint:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, CGPoint, nint>)&CharacterAt,
            "Q@:{CGPoint=dd}")
        .AddProtocol("NSTextInputClient");

    private static MacOSWindowWorker Worker(IntPtr self) => ObjCManagedObject.Get<MacOSWindowWorker>(self);

    private static MacOSTextInput Input(IntPtr self) => Worker(self)?.TextInput;

    [UnmanagedCallersOnly]
    private static void Insert(IntPtr self, IntPtr selector, IntPtr text, NSRange replaced) =>
        Input(self)?.Insert(NSAttributedString.TextOf(text), replaced);

    [UnmanagedCallersOnly]
    private static void Compose(IntPtr self, IntPtr selector, IntPtr text, NSRange selected, NSRange replaced) =>
        Input(self)?.Compose(NSAttributedString.TextOf(text), selected, replaced);

    [UnmanagedCallersOnly]
    private static void Commit(IntPtr self, IntPtr selector) => Input(self)?.Commit();

    [UnmanagedCallersOnly]
    private static void Command(IntPtr self, IntPtr selector, IntPtr command) => Input(self)?.Command();

    [UnmanagedCallersOnly]
    private static byte IsComposing(IntPtr self, IntPtr selector) => Input(self)?.IsComposing == true ? (byte)1 : (byte)0;

    [UnmanagedCallersOnly]
    private static NSRange MarkedRange(IntPtr self, IntPtr selector) => Input(self)?.MarkedRange ?? NSRange.None;

    [UnmanagedCallersOnly]
    private static NSRange SelectedRange(IntPtr self, IntPtr selector) => Input(self)?.SelectedRange ?? NSRange.None;

    [UnmanagedCallersOnly]
    private static IntPtr Part(IntPtr self, IntPtr selector, NSRange range, NSRange* actual)
    {
        var found = NSRange.None;
        var text = Input(self)?.Part(range, out found);
        if (actual != null)
            *actual = found;
        return text == null ? IntPtr.Zero : NSAttributedString.Create(text);
    }

    [UnmanagedCallersOnly]
    private static IntPtr Attributes(IntPtr self, IntPtr selector) => NSArray.Create([]);

    [UnmanagedCallersOnly]
    private static CGRect Caret(IntPtr self, IntPtr selector, NSRange range, NSRange* actual)
    {
        if (actual != null)
            *actual = range;
        return Worker(self)?.CaretOnScreen ?? default;
    }

    [UnmanagedCallersOnly]
    private static nint CharacterAt(IntPtr self, IntPtr selector, CGPoint point) => NSRange.NotFound;
}
