using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Alt pressed and released with nothing else between completes the gesture; a modifier held before it (the
/// layout switch, AltGr), any other key between, even one seen only as it comes up, or a cancel make it half of
/// something else.</summary>
[TestFixture]
public class AltPressedAloneTests
{
    private static KeyEventArgs Key(Key key, InputModifiers modifiers = InputModifiers.None) =>
        new(KeyboardDevice.CurrentDevice, key, modifiers, 0);

    [Test]
    public void AltPressedAndReleasedAlone_Completes()
    {
        var alt = new AltPressedAlone();

        alt.KeyDown(Key(Adamantium.UI.Core.Input.Key.LeftAlt, InputModifiers.LeftAlt));

        Assert.That(alt.KeyUp(Key(Adamantium.UI.Core.Input.Key.LeftAlt)), Is.True);
    }

    [Test]
    public void AltHeldAndRepeating_StillCompletes()
    {
        var alt = new AltPressedAlone();

        alt.KeyDown(Key(Adamantium.UI.Core.Input.Key.LeftAlt, InputModifiers.LeftAlt));
        alt.KeyDown(Key(Adamantium.UI.Core.Input.Key.LeftAlt, InputModifiers.LeftAlt));

        Assert.That(alt.KeyUp(Key(Adamantium.UI.Core.Input.Key.LeftAlt)), Is.True);
    }

    [Test]
    public void ShiftThenAlt_TheLayoutSwitch_DoesNotComplete()
    {
        var alt = new AltPressedAlone();

        alt.KeyDown(Key(Adamantium.UI.Core.Input.Key.LeftShift, InputModifiers.LeftShift));
        alt.KeyDown(Key(Adamantium.UI.Core.Input.Key.LeftAlt, InputModifiers.LeftShift | InputModifiers.LeftAlt));
        alt.KeyUp(Key(Adamantium.UI.Core.Input.Key.LeftShift, InputModifiers.LeftAlt));

        Assert.That(alt.KeyUp(Key(Adamantium.UI.Core.Input.Key.LeftAlt)), Is.False);
    }

    [Test]
    public void AltGr_CtrlThenAlt_DoesNotComplete()
    {
        var alt = new AltPressedAlone();

        alt.KeyDown(Key(Adamantium.UI.Core.Input.Key.LeftCtrl, InputModifiers.LeftControl));
        alt.KeyDown(Key(Adamantium.UI.Core.Input.Key.RightAlt, InputModifiers.LeftControl | InputModifiers.RightAlt));

        Assert.That(alt.KeyUp(Key(Adamantium.UI.Core.Input.Key.RightAlt, InputModifiers.LeftControl)), Is.False);
    }

    [Test]
    public void AltWithAnotherKey_AShortcut_DoesNotComplete()
    {
        var alt = new AltPressedAlone();

        alt.KeyDown(Key(Adamantium.UI.Core.Input.Key.LeftAlt, InputModifiers.LeftAlt));
        alt.KeyDown(Key(Adamantium.UI.Core.Input.Key.F, InputModifiers.LeftAlt));
        alt.KeyUp(Key(Adamantium.UI.Core.Input.Key.F, InputModifiers.LeftAlt));

        Assert.That(alt.KeyUp(Key(Adamantium.UI.Core.Input.Key.LeftAlt)), Is.False);
    }

    [Test]
    public void AKeySeenOnlyAsItComesUp_PrintScreen_DoesNotLetAltComplete()
    {
        var alt = new AltPressedAlone();

        alt.KeyDown(Key(Adamantium.UI.Core.Input.Key.LeftAlt, InputModifiers.LeftAlt));
        alt.KeyUp(Key(Adamantium.UI.Core.Input.Key.PrintScreen, InputModifiers.LeftAlt));

        Assert.That(alt.KeyUp(Key(Adamantium.UI.Core.Input.Key.LeftAlt)), Is.False);
    }

    [Test]
    public void Cancelled_APointerPressedUnderAlt_DoesNotComplete()
    {
        var alt = new AltPressedAlone();

        alt.KeyDown(Key(Adamantium.UI.Core.Input.Key.LeftAlt, InputModifiers.LeftAlt));
        alt.Cancel();

        Assert.That(alt.KeyUp(Key(Adamantium.UI.Core.Input.Key.LeftAlt)), Is.False);
    }
}
