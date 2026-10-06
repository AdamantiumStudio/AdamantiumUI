using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls;

/// <summary>A drag grip for an item template: only it starts the drag, so the rest of the row stays interactive. Themed; any
/// other element can use the attached <c>DragDrop.IsDragHandle</c>.</summary>
public class DragHandle : Control, IDragHandle
{
    public DragHandle()
    {
        Cursor = Cursors.SizeAll;   // the grip advertises itself before the press
    }

    /// <summary>Whether the grip is in force. False and the source drags by its whole body again - bind a "drag only by
    /// the handle" switch here rather than hiding the control, so the grip stays where the eye expects it.</summary>
    public static readonly AdamantiumProperty IsActiveProperty = AdamantiumProperty.Register(nameof(IsActive),
        typeof(bool), typeof(DragHandle), new PropertyMetadata(true));

    public bool IsActive
    {
        get => GetValue<bool>(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    bool IDragHandle.IsDragHandleActive => IsActive;

    protected override AutomationPeer OnCreateAutomationPeer() =>
        TemplatedParent == null ? new ThumbAutomationPeer(this) : null;
}
