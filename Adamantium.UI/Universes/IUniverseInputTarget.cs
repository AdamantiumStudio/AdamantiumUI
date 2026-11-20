using Adamantium.Multiverse.Input;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Universes;

/// <summary>Where a <see cref="UniverseInputListener"/> gives the input it translated: a universe in this process, or one
/// in another.</summary>
internal interface IUniverseInputTarget
{
    /// <summary>The surface's pixels to a point.</summary>
    float PixelsPerPoint { get; }

    /// <summary>Whether keys typed now go to the game.</summary>
    bool TakesKeyboard { get; }

    bool IsKeyDown(Keys key);

    /// <summary>Whether a press that reached the element is meant for the universe; a release always is.</summary>
    bool IsUniverseInput(RoutedEventArgs e);

    /// <summary>Whether a key the universe took is marked handled.</summary>
    bool SpendsKey(KeyEventArgs e);

    void TakeKeyboardInput(KeyboardInput input);

    void TakeMouseInput(MouseInput input);

    void TakeGestureInput(GestureInput input);

    void TakeText(string text);
}
