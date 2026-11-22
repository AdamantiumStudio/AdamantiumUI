using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Platforms.Windows;
using Adamantium.Win32;
using NUnit.Framework;

namespace Adamantium.UITests;

// What an on-screen keyboard puts into the system's input for another application. Built here, never sent: a test
// sending keys would type into whatever window is in front.
[TestFixture]
public class SystemTypingTests
{
    [Test]
    public void TheInput_HasTheSizeTheSystemTakes()
    {
        // SendInput refuses every event of another size: 40 bytes on 64-bit, 28 on 32-bit.
        Assert.That(Marshal.SizeOf<INPUT>(), Is.EqualTo(System.Environment.Is64BitProcess ? 40 : 28));
    }

    [Test]
    public void Text_GoesAsItsCharacters_EachDownAndUp_WhateverTheLayout()
    {
        var inputs = WindowsInput.TextInputs("ё😀");

        Assert.That(inputs, Has.Length.EqualTo(6), "ё, and the two halves of the emoji, each down and up");
        Assert.Multiple(() =>
        {
            Assert.That(inputs[0].Data.Keyboard.ScanCode, Is.EqualTo('ё'));
            Assert.That(inputs[0].Data.Keyboard.Flags, Is.EqualTo(KeyEventFlags.Unicode));
            Assert.That(inputs[0].Data.Keyboard.VirtualKey, Is.Zero);
            Assert.That(inputs[1].Data.Keyboard.Flags, Is.EqualTo(KeyEventFlags.Unicode | KeyEventFlags.KeyUp));
            Assert.That(inputs[2].Data.Keyboard.ScanCode, Is.EqualTo("😀"[0]));
            Assert.That(inputs[4].Data.Keyboard.ScanCode, Is.EqualTo("😀"[1]));
        });
    }

    [Test]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void AKey_GoesByItsCodeAndPlace_TheArrowsAsTheExtendedOnes()
    {
        Assume.That(System.OperatingSystem.IsWindows(), "The scan codes come from the system's layout.");
        var backspace = WindowsInput.KeyInput(Key.BackSpace, down: true);
        var leftUp = WindowsInput.KeyInput(Key.LeftArrow, down: false);
        var numLock = WindowsInput.KeyInput(Key.NumLock, down: true);

        Assert.Multiple(() =>
        {
            Assert.That(backspace.Type, Is.EqualTo(InputType.Keyboard));
            Assert.That(backspace.Data.Keyboard.VirtualKey, Is.EqualTo(0x08));
            Assert.That(backspace.Data.Keyboard.ScanCode, Is.EqualTo(0x0E), "Where Backspace is on the keyboard");
            Assert.That(backspace.Data.Keyboard.Flags, Is.EqualTo((KeyEventFlags)0));
            Assert.That(leftUp.Data.Keyboard.VirtualKey, Is.EqualTo(0x25));
            Assert.That(leftUp.Data.Keyboard.ScanCode, Is.EqualTo(0x4B));
            Assert.That(leftUp.Data.Keyboard.Flags, Is.EqualTo(KeyEventFlags.KeyUp | KeyEventFlags.ExtendedKey),
                "Not the numeric pad's 4");
            Assert.That(numLock.Data.Keyboard.Flags, Is.EqualTo(KeyEventFlags.ExtendedKey));
        });
    }

    [Test]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void ALineBreakOrATab_GoesAsItsKey()
    {
        Assume.That(System.OperatingSystem.IsWindows());
        var inputs = WindowsInput.TextInputs("a\r\nb\tc\n");

        Assert.That(inputs.Select(input => input.Data.Keyboard.VirtualKey == 0
                ? ((char)input.Data.Keyboard.ScanCode).ToString()
                : $"[{input.Data.Keyboard.VirtualKey:X2}]")
            .Where((_, index) => index % 2 == 0), Is.EqualTo(new[] { "a", "[0D]", "b", "[09]", "c", "[0D]" }));
    }
}
