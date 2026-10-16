using System;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;

namespace Adamantium.UI.Controls;

/// <summary>A context menu about to open: against what, and at which point of it - null when the keyboard opened it.</summary>
public sealed class ContextMenuOpeningEventArgs(UIComponent target, Vector2? point) : EventArgs
{
    public UIComponent Target { get; } = target;

    public Vector2? Point { get; } = point;
}
