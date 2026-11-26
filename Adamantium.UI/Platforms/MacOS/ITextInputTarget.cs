using Adamantium.UI.Core.Input.Raw;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>What text goes to.</summary>
internal interface ITextInputTarget
{
    /// <summary>Text typed.</summary>
    void Type(string text);

    /// <summary>Text being composed, with the cursor in it.</summary>
    void Compose(RawTextCompositionEventType type, string text, int cursor);

    /// <summary>Text typed before the caret is taken back.</summary>
    void Erase(string text);
}
