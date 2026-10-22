using Adamantium.UI.Platforms.Wayland;
using NUnit.Framework;

namespace Adamantium.UITests;

public class WaylandTextInputTests
{
    [TestCase("にほんご", 0, 0)]
    [TestCase("にほんご", 6, 2)]
    [TestCase("にほんご", 12, 4)]
    [TestCase("ab", 1, 1)]
    [TestCase("にほんご", -1, 4)]
    public void APreeditCursor_InUtf8Bytes_FallsOnItsCharacter(string text, int bytes, int character) =>
        Assert.That(WaylandTextInput.CharacterIndex(text, bytes), Is.EqualTo(character));
}
