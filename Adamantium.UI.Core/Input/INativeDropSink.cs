using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

/// <summary>What a native drop target calls, so OS drags behave like in-app ones. Runs on the platform thread inside the
/// source's modal loop and must return promptly.</summary>
public interface INativeDropSink
{
    /// <summary>The pointer entered <paramref name="window"/> carrying <paramref name="data"/> (already read out of the
    /// OS payload into a managed package). Returns the effect to show.</summary>
    DragDropEffects DragEnter(IWindow window, IDataPackage data, PixelPoint screenPoint, InputModifiers modifiers, DragDropEffects allowed);

    /// <summary>The pointer moved inside <paramref name="window"/> during a native drag. Returns the effect to show.</summary>
    DragDropEffects DragOver(IWindow window, PixelPoint screenPoint, InputModifiers modifiers, DragDropEffects allowed);

    /// <summary>The pointer left <paramref name="window"/>, or the native drag was canceled over it.</summary>
    void DragLeave(IWindow window);

    /// <summary>The payload was released over <paramref name="window"/>. Returns the effect actually applied - the OS
    /// reports it back to the drag source (a Move tells the source app to delete the original, so never answer Move
    /// unless the drop really consumed it).</summary>
    DragDropEffects Drop(IWindow window, IDataPackage data, PixelPoint screenPoint, InputModifiers modifiers, DragDropEffects allowed);
}
