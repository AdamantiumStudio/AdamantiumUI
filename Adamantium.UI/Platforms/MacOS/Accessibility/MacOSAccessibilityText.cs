using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.UI.Platforms.MacOS.Accessibility;

/// <summary>An element's text as the Mac's accessibility reads it - characters, lines, the selection and the caret, where
/// a piece of it is - so VoiceOver reads a field by character, word and line and selects in it. Offered only by an
/// element whose peer tells its text; a password tells none.</summary>
internal static unsafe class MacOSAccessibilityText
{
    private const string Range = "{_NSRange=QQ}";
    private const string Rectangle = "{CGRect={CGPoint=dd}{CGSize=dd}}";
    private static readonly IntPtr AutoreleaseSelector = ObjCRuntime.RegisterSelector("autorelease");

    /// <summary>The getters offered only where there is text.</summary>
    internal static readonly HashSet<string> Selectors =
    [
        "accessibilityNumberOfCharacters", "accessibilitySelectedText", "accessibilitySelectedTextRange",
        "accessibilityVisibleCharacterRange", "accessibilityStringForRange:", "accessibilityLineForIndex:",
        "accessibilityRangeForLine:", "accessibilityFrameForRange:", "accessibilityRangeForPosition:",
        "accessibilityInsertionPointLineNumber", "accessibilityAttributedStringForRange:", "accessibilityRangeForIndex:",
        "accessibilityStyleRangeForIndex:"
    ];

    internal static ObjCClassBuilder AddText(this ObjCClassBuilder element) => element
        .AddMethod("accessibilityNumberOfCharacters", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint>)&Count, "q@:")
        .AddMethod("accessibilitySelectedText", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&SelectedText, "@@:")
        .AddMethod("accessibilitySelectedTextRange", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange>)&SelectedRange, $"{Range}@:")
        .AddMethod("setAccessibilitySelectedTextRange:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange, void>)&Select,
            $"v@:{Range}")
        .AddMethod("accessibilityVisibleCharacterRange", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange>)&VisibleRange,
            $"{Range}@:")
        .AddMethod("accessibilityStringForRange:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange, IntPtr>)&StringFor,
            $"@@:{Range}")
        .AddMethod("accessibilityLineForIndex:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint, nint>)&LineFor, "q@:q")
        .AddMethod("accessibilityRangeForLine:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint, NSRange>)&RangeForLine,
            $"{Range}@:q")
        .AddMethod("accessibilityFrameForRange:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange, CGRect>)&FrameFor,
            $"{Rectangle}@:{Range}")
        .AddMethod("accessibilityRangeForPosition:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, CGPoint, NSRange>)&RangeAt,
            $"{Range}@:{{CGPoint=dd}}")
        .AddMethod("accessibilityInsertionPointLineNumber", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint>)&InsertionLine,
            "q@:")
        // What VoiceOver reads a field through, moving by word and character: the plain text of a range, the whole
        // character at an index, the run of one style.
        .AddMethod("accessibilityAttributedStringForRange:",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange, IntPtr>)&AttributedStringFor, $"@@:{Range}")
        .AddMethod("accessibilityRangeForIndex:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint, NSRange>)&RangeForIndex,
            $"{Range}@:q")
        .AddMethod("accessibilityStyleRangeForIndex:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint, NSRange>)&StyleRange,
            $"{Range}@:q");

    [UnmanagedCallersOnly]
    private static IntPtr AttributedStringFor(IntPtr self, IntPtr selector, NSRange range) =>
        Node(self)?.StringFor(range) is { } text ? NSAttributedString.Create(text) : IntPtr.Zero;

    [UnmanagedCallersOnly]
    private static NSRange RangeForIndex(IntPtr self, IntPtr selector, nint index) => Node(self)?.CharacterAt(index) ?? default;

    [UnmanagedCallersOnly]
    private static NSRange StyleRange(IntPtr self, IntPtr selector, nint index) => Node(self)?.StyleRangeAt(index) ?? default;

    [UnmanagedCallersOnly]
    private static nint Count(IntPtr self, IntPtr selector) => Node(self)?.CharacterCount ?? 0;

    [UnmanagedCallersOnly]
    private static IntPtr SelectedText(IntPtr self, IntPtr selector) => Text(Node(self)?.SelectedText);

    [UnmanagedCallersOnly]
    private static NSRange SelectedRange(IntPtr self, IntPtr selector) => Node(self)?.SelectedRange ?? default;

    [UnmanagedCallersOnly]
    private static void Select(IntPtr self, IntPtr selector, NSRange range) => Node(self)?.SelectRange(range);

    [UnmanagedCallersOnly]
    private static NSRange VisibleRange(IntPtr self, IntPtr selector) => Node(self)?.VisibleRange ?? default;

    [UnmanagedCallersOnly]
    private static IntPtr StringFor(IntPtr self, IntPtr selector, NSRange range) => Text(Node(self)?.StringFor(range));

    [UnmanagedCallersOnly]
    private static nint LineFor(IntPtr self, IntPtr selector, nint index) => Node(self)?.LineOf(index) ?? 0;

    [UnmanagedCallersOnly]
    private static NSRange RangeForLine(IntPtr self, IntPtr selector, nint line) => Node(self)?.RangeOfLine(line) ?? default;

    [UnmanagedCallersOnly]
    private static CGRect FrameFor(IntPtr self, IntPtr selector, NSRange range) => Node(self)?.FrameFor(range) ?? default;

    [UnmanagedCallersOnly]
    private static NSRange RangeAt(IntPtr self, IntPtr selector, CGPoint point) => Node(self)?.RangeAt(point) ?? default;

    [UnmanagedCallersOnly]
    private static nint InsertionLine(IntPtr self, IntPtr selector) => Node(self)?.InsertionLine ?? 0;

    private static MacOSAccessibilityNode Node(IntPtr element) => MacOSAccessibilityElement.NodeOf(element);

    private static IntPtr Text(string text) =>
        text == null ? IntPtr.Zero : NSObject.Send(NSString.Create(text), AutoreleaseSelector);
}
