using Adamantium.UI.Core.Input;

namespace Adamantium.UITests;

/// <summary>A system keyboard with every key up and no lock lit.</summary>
internal sealed class AllKeysUpKeyboard : INativeKeyboard
{
    public bool IsKeyDown(Key key) => false;

    public bool IsKeyToggled(Key key) => false;

    public bool HasRightToLeftLayout => false;
}
