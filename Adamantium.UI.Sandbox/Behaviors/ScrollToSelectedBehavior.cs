using System;
using Adamantium.UI.Controls;
using Adamantium.UI.Core.Behaviors;

namespace Adamantium.UI.Sandbox.Behaviors;

/// <summary>Keeps the selected row of a list in view when it is chosen from outside the list, by its view-model.</summary>
public class ScrollToSelectedBehavior : Behavior<ListBox>
{
    protected override void OnAttached(ListBox component) => component.SelectionChanged += OnSelectionChanged;

    protected override void OnDetached(ListBox component) => component.SelectionChanged -= OnSelectionChanged;

    private static void OnSelectionChanged(object sender, EventArgs e)
    {
        if (sender is ListBox { SelectedItem: { } item } list) list.ScrollIntoView(item);
    }
}
