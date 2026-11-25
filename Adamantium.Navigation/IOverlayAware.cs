using System;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.Navigation;

/// <summary>Lifecycle a view model opts into to be shown as a floating in-window <c>OverlayWindow</c> (a window, NOT a
/// modal dialog - see <see cref="IOverlayAware"/> vs <see cref="IDialogAware"/>): it supplies a title, is told when it
/// opens, and closes itself (with a result) by raising <see cref="RequestClose"/>.</summary>
public interface IOverlayAware
{
    /// <summary>Shown on the overlay window's title bar. May change dynamically - raise PropertyChanged (INotifyPropertyChanged)
    /// and the bar updates live; get-only here because the host only reads it (the view model owns how it is stored).</summary>
    string Title { get; }

    /// <summary>Optional title-bar icon (any content the title bar's icon slot can present - a glyph, a Path, a small
    /// control). Null (default) shows no icon.</summary>
    object Icon => null;

    /// <summary>The window's automation id: what automation finds it by when its title changes. Null (default) leaves the
    /// window's own.</summary>
    string AutomationId => null;

    // Presentation options the overlay window is created with. Default interface members - a view model overrides only the
    // ones that differ from the default (a plain floating, movable, resizable, pinnable, closable, non-modal window).
    bool AllowMove => true;
    bool CanResize => true;
    bool CanPin => true;
    bool CanCollapse => false;
    bool CanClose => true;
    bool IsModal => false;

    /// <summary>Where the window first appears. Default <see cref="OverlayStartupLocation.CenterOwner"/>; use
    /// <see cref="OverlayStartupLocation.Manual"/> to open it at <see cref="Left"/>/<see cref="Top"/>, or a corner such as
    /// <see cref="OverlayStartupLocation.BottomRight"/> to open it that far from the corner's edges.</summary>
    OverlayStartupLocation StartupLocation => OverlayStartupLocation.CenterOwner;

    /// <summary>The window's left/top, in the parent window's coordinates (like <c>Window.Left</c>/<c>Window.Top</c>) -
    /// opened at a right or bottom corner, the room from that edge until the window is dragged, resized or collapsed.
    /// TWO-WAY bound to the window: assign it (raise PropertyChanged) and the window moves; drag the window and it updates.
    /// Used as the opening position unless <see cref="StartupLocation"/> centers it. Override with settable, notifying
    /// properties to use them (the defaults here are inert).</summary>
    double Left { get => 0; set { } }
    double Top { get => 0; set { } }

    /// <summary>The window's width; NaN (default) sizes it to its content - collapsed, to its title. TWO-WAY bound like
    /// <see cref="Left"/>: a width given here holds collapsed and expanded alike, and a resize by the grip updates it.
    /// </summary>
    double Width { get => double.NaN; set { } }

    /// <summary>Whether the window is collapsed to its title (with <see cref="CanCollapse"/>). TWO-WAY bound like
    /// <see cref="Left"/>: true when it opens, it opens collapsed; folding it by its button updates it.</summary>
    bool IsCollapsed { get => false; set { } }

    /// <summary>Awaited before the window appears. One that raises <see cref="RequestClose"/> meanwhile never appears.</summary>
    Task OnOverlayOpenedAsync(NavigationParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>Raise to close the overlay with a result. The x button / Escape also close it (with a null result).</summary>
    event Action<object> RequestClose;
}
