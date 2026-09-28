using System;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.DataGrid;

/// <summary>A row's pinned, opaque left strip: its ordinal among visible rows and the handle for the whole row. Without a
/// number it is the corner that selects the table.</summary>
public class DataGridRowHeader : ContentControl
{
    /// <summary>The row's place in the visible order, from 1. Zero on the corner, which stands for no row.</summary>
    public static readonly AdamantiumProperty NumberProperty = AdamantiumProperty.Register(nameof(Number),
        typeof(Int32), typeof(DataGridRowHeader),
        new PropertyMetadata(0, PropertyMetadataOptions.AffectsRender, OnNumberChanged));

    /// <summary>Every cell of this row is selected - the strip shows it, so a row picked by its number reads as picked.</summary>
    public static readonly AdamantiumProperty IsSelectedProperty = AdamantiumProperty.Register(nameof(IsSelected),
        typeof(bool), typeof(DataGridRowHeader), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    /// <summary>This is the CORNER, not a row: it has no number and it answers for the whole table.</summary>
    public static readonly AdamantiumProperty IsCornerProperty = AdamantiumProperty.Register(nameof(IsCorner),
        typeof(bool), typeof(DataGridRowHeader), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    // NOT clipped: the strip holds a number, which TextTrimming keeps inside the strip on its own. A clip is a scissor
    // and a scissor ends the batch - see DataGridCell for what that costs a table per element.

    public Int32 Number
    {
        get => GetValue<Int32>(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue<bool>(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool IsCorner
    {
        get => GetValue<bool>(IsCornerProperty);
        set => SetValue(IsCornerProperty, value);
    }

    // The number IS the content, so a theme styles it as it styles any other content and needs to know nothing about
    // rows. A corner shows nothing at all.
    private static void OnNumberChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not DataGridRowHeader header) return;

        header.Content = header.Number > 0 ? header.Number.ToString() : null;
    }
}
