using System;
using System.Collections.Generic;

namespace Adamantium.UI.Core;

/// <summary>A row of a <see cref="INotificationAreaIcon"/>'s menu as a desktop that shows the menu itself takes it - a
/// Linux panel: its text, whether it can be picked and is checked, its own rows, and what picking it does.</summary>
public sealed class NotificationAreaMenuItem
{
    /// <summary>A line between rows.</summary>
    public static NotificationAreaMenuItem Separator { get; } = new() { IsSeparator = true };

    public string Text { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>The row is turned on and off, and shows a check mark while <see cref="IsChecked"/>.</summary>
    public bool IsCheckable { get; init; }

    public bool IsChecked { get; init; }

    public bool IsSeparator { get; init; }

    /// <summary>The rows of its submenu; empty for a row that is picked.</summary>
    public IReadOnlyList<NotificationAreaMenuItem> Items { get; init; } = [];

    /// <summary>Run on the application's loop thread when the row is picked.</summary>
    public Action Picked { get; init; }
}
