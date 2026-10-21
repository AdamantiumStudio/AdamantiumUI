using System;

namespace Adamantium.UI.Core.Input;

/// <summary>The OS drag-drop bridge to other applications: drop-in through a native target on every window feeding an
/// <see cref="INativeDropSink"/>, and drag-out through <see cref="BeginDrag"/>.</summary>
public interface INativeDragDrop
{
    /// <summary>False when the OS bridge could not be brought up (on Windows: the UI thread is not an STA OLE thread).
    /// The in-app drag-drop keeps working; only the crossing to other applications is off.</summary>
    bool IsAvailable { get; }

    /// <summary>True where only the OS knows which window a drag is over (Wayland): every mouse drag then goes through
    /// <see cref="BeginDrag"/>.</summary>
    bool CarriesInAppDrags => false;

    /// <summary>Make <paramref name="window"/> a native drop target, delivering to <paramref name="sink"/>. Registered
    /// ONCE at window creation and left on for the window's life - never per drag. Call on the thread that owns the
    /// native window.</summary>
    void RegisterDropTarget(IWindow window, INativeDropSink sink);

    /// <summary>Drop the native drop target registered for <paramref name="window"/> (called as the window closes,
    /// before the native handle is destroyed).</summary>
    void UnregisterDropTarget(IWindow window);

    /// <summary>Hands an in-app drag to the OS without blocking; false if it could not start. The outcome arrives via
    /// <paramref name="completed"/> on the UI thread; <paramref name="ghost"/> is the OS drag image. A drag that is not
    /// <paramref name="external"/> reaches the application's own windows only.</summary>
    bool BeginDrag(IWindow source, IDataPackage data, DragDropEffects allowed, DragGhostImage ghost, Action<DragDropEffects> completed,
        bool external);
}
