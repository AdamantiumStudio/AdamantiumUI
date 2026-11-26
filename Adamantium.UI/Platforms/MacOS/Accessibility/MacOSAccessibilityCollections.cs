using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Platforms.MacOS.Accessibility;

/// <summary>What the Mac's accessibility asks of lists, tabs, tables and outlines: what is selected and selecting, an
/// item's place among its own, a tab group's tabs, a table's rows, columns, headers and cells, an outline row's depth,
/// openness and the rows under and over it. Offered only by an element that is such a thing.</summary>
internal static unsafe class MacOSAccessibilityCollections
{
    private const string Range = "{_NSRange=QQ}";

    /// <summary>The methods offered only where the element is such a thing.</summary>
    internal static readonly string[] Selectors =
    [
        "accessibilitySelectedChildren", "setAccessibilitySelectedChildren:", "accessibilityIndex", "accessibilityTabs",
        "accessibilityRowCount", "accessibilityColumnCount", "accessibilityCellForColumn:row:", "accessibilityRows",
        "accessibilityColumnHeaderUIElements", "accessibilityRowIndexRange", "accessibilityColumnIndexRange",
        "accessibilityDisclosureLevel", "isAccessibilityDisclosed", "setAccessibilityDisclosed:",
        "accessibilityDisclosedRows", "accessibilityDisclosedByRow"
    ];

    internal static ObjCClassBuilder AddCollections(this ObjCClassBuilder element) => element
        .AddMethod("accessibilitySelectedChildren", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&SelectedChildren, "@@:")
        .AddMethod("setAccessibilitySelectedChildren:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Select, "v@:@")
        .AddMethod("accessibilityIndex", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint>)&Index, "q@:")
        .AddMethod("accessibilityTabs", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Tabs, "@@:")
        .AddMethod("accessibilityRowCount", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint>)&RowCount, "q@:")
        .AddMethod("accessibilityColumnCount", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint>)&ColumnCount, "q@:")
        .AddMethod("accessibilityCellForColumn:row:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint, nint, IntPtr>)&Cell, "@@:qq")
        .AddMethod("accessibilityRows", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Rows, "@@:")
        .AddMethod("accessibilityColumnHeaderUIElements", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&ColumnHeaders, "@@:")
        .AddMethod("accessibilityRowIndexRange", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange>)&RowRange, $"{Range}@:")
        .AddMethod("accessibilityColumnIndexRange", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, NSRange>)&ColumnRange, $"{Range}@:")
        .AddMethod("accessibilityDisclosureLevel", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint>)&Level, "q@:")
        .AddMethod("isAccessibilityDisclosed", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&IsDisclosed, "B@:")
        .AddMethod("setAccessibilityDisclosed:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte, void>)&SetDisclosed, "v@:B")
        .AddMethod("accessibilityDisclosedRows", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&DisclosedRows, "@@:")
        .AddMethod("accessibilityDisclosedByRow", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&DisclosedBy, "@@:");

    [UnmanagedCallersOnly]
    private static IntPtr SelectedChildren(IntPtr self, IntPtr selector) => Array(self, node => node.Selected);

    [UnmanagedCallersOnly]
    private static void Select(IntPtr self, IntPtr selector, IntPtr chosen)
    {
        if (Node(self) is not { } node || chosen == IntPtr.Zero)
            return;

        var items = new List<AutomationPeer>();
        for (var i = 0; i < NSArray.CountOf(chosen); i++)
        {
            if (MacOSAccessibilityElement.NodeOf(NSArray.ObjectAt(chosen, i))?.Peer is { } peer)
                items.Add(peer);
        }

        node.Select(items);
    }

    [UnmanagedCallersOnly]
    private static nint Index(IntPtr self, IntPtr selector) => Node(self)?.Index ?? 0;

    [UnmanagedCallersOnly]
    private static IntPtr Tabs(IntPtr self, IntPtr selector) => Array(self, node => node.Tabs);

    [UnmanagedCallersOnly]
    private static nint RowCount(IntPtr self, IntPtr selector) => Node(self)?.RowCount ?? 0;

    [UnmanagedCallersOnly]
    private static nint ColumnCount(IntPtr self, IntPtr selector) => Node(self)?.ColumnCount ?? 0;

    [UnmanagedCallersOnly]
    private static IntPtr Cell(IntPtr self, IntPtr selector, nint column, nint row) =>
        Node(self) is { } node ? node.Owner.Lent(node.CellAt(column, row)) : IntPtr.Zero;

    [UnmanagedCallersOnly]
    private static IntPtr Rows(IntPtr self, IntPtr selector) => Array(self, node => node.Rows);

    [UnmanagedCallersOnly]
    private static IntPtr ColumnHeaders(IntPtr self, IntPtr selector) => Array(self, node => node.ColumnHeaders);

    [UnmanagedCallersOnly]
    private static NSRange RowRange(IntPtr self, IntPtr selector) => Node(self)?.RowRange ?? NSRange.None;

    [UnmanagedCallersOnly]
    private static NSRange ColumnRange(IntPtr self, IntPtr selector) => Node(self)?.ColumnRange ?? NSRange.None;

    [UnmanagedCallersOnly]
    private static nint Level(IntPtr self, IntPtr selector) => Node(self)?.DisclosureLevel ?? 0;

    [UnmanagedCallersOnly]
    private static byte IsDisclosed(IntPtr self, IntPtr selector) => Node(self)?.IsDisclosed == true ? (byte)1 : (byte)0;

    [UnmanagedCallersOnly]
    private static void SetDisclosed(IntPtr self, IntPtr selector, byte disclosed) => Node(self)?.SetDisclosed(disclosed != 0);

    [UnmanagedCallersOnly]
    private static IntPtr DisclosedRows(IntPtr self, IntPtr selector) => Array(self, node => node.DisclosedRows);

    [UnmanagedCallersOnly]
    private static IntPtr DisclosedBy(IntPtr self, IntPtr selector) =>
        Node(self) is { } node ? node.Owner.Lent(node.DisclosedBy) : IntPtr.Zero;

    private static MacOSAccessibilityNode Node(IntPtr element) => MacOSAccessibilityElement.NodeOf(element);

    private static IntPtr Array(IntPtr self, Func<MacOSAccessibilityNode, IReadOnlyList<AutomationPeer>> peers) =>
        Node(self) is { } node ? node.Owner.ArrayOf(peers(node)) : NSArray.Create([]);
}
