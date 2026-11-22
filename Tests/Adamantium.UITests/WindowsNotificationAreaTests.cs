using System;
using Adamantium.UI.Platforms.Windows;
using Adamantium.Win32;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The hidden window the Windows notification area's icons tell what is done to them.</summary>
[Platform("Win")]
public class WindowsNotificationAreaTests
{
    [Test]
    public void TheShellRestarting_IsLetThroughToTheWindow()
    {
        var taskbarCreated = Messages.RegisterWindowMessage("TaskbarCreated");
        using var plain = MakeWindow();
        using var heard = MakeWindow();

        Assert.That(WindowsNotificationArea.HearShellRestarting(heard.Handle), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(StatusBeforeAllowing(plain.Handle, taskbarCreated), Is.Not.EqualTo(
                MessageFilterStatus.AlreadyAllowedForWindow), "precondition: a window does not let it through by itself");
            Assert.That(StatusBeforeAllowing(heard.Handle, taskbarCreated),
                Is.EqualTo(MessageFilterStatus.AlreadyAllowedForWindow));
        });
    }

    private static Win32NativeWindowWrapper MakeWindow() =>
        new($"AdamantiumTestWindow {Guid.NewGuid()}", 0, 0, 0, 0, 0, 0, 0, IntPtr.Zero);

    private static MessageFilterStatus StatusBeforeAllowing(IntPtr window, uint message)
    {
        var status = CHANGEFILTERSTRUCT.Create();
        Assert.That(Messages.ChangeWindowMessageFilterEx(window, message, MessageFilterAction.Allow, ref status), Is.True);
        return status.Status;
    }
}
