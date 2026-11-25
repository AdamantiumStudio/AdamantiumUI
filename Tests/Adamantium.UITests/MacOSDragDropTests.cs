using System;
using System.Text;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Platforms.MacOS;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What a drag carries to and from other applications on macOS, on a pasteboard of the test's own.</summary>
[TestFixture]
[Platform("MacOsX")]
public class MacOSDragDropTests
{
    private IntPtr _pool;
    private IntPtr _pasteboard;

    [SetUp]
    public void SetUp()
    {
        _pool = ObjCRuntime.PushAutoreleasePool();
        _pasteboard = NSPasteboard.CreateUnique();
    }

    [TearDown]
    public void TearDown()
    {
        NSPasteboard.ReleaseGlobally(_pasteboard);
        ObjCRuntime.PopAutoreleasePool(_pool);
    }

    [Test]
    public void FilesAndText_ComeAsTheyWent()
    {
        var sent = new DataPackage();
        sent.Set(DataFormats.Files, new[] { "/tmp/й 1.txt", "/tmp/b" });
        sent.Set(DataFormats.Text, "Привет");
        Put(sent);

        var read = new MacOSClipboard(_pasteboard).Read();

        Assert.Multiple(() =>
        {
            Assert.That(read.Get(DataFormats.Files), Is.EqualTo(new[] { "/tmp/й 1.txt", "/tmp/b" }));
            Assert.That(read.Get(DataFormats.Text), Is.EqualTo("Привет"));
            Assert.That(read.Contains(DataFormats.Image), Is.False);
        });
    }

    [Test]
    public void APromise_IsMadeWhenItIsAskedFor()
    {
        var made = 0;
        var sent = new DataPackage();
        sent.SetDeferred(DataFormats.Text, () =>
        {
            made++;
            return "late";
        });
        Put(sent);
        Assert.That(made, Is.Zero, "offered, not made");
        Assert.That(new MacOSClipboard(_pasteboard).Contains(DataFormats.Text), Is.True);

        var read = new MacOSClipboard(_pasteboard).Read();

        Assert.That(read.Get(DataFormats.Text), Is.EqualTo("late"));
        Assert.That(made, Is.EqualTo(1));
    }

    [Test]
    public void APromisedFile_IsAFile()
    {
        var sent = new DataPackage();
        sent.SetDeferred(DataFormats.Files, () => new[] { "/tmp/a.png" });
        Put(sent);

        Assert.That(new MacOSClipboard(_pasteboard).Read().Get(DataFormats.Files), Is.EqualTo(new[] { "/tmp/a.png" }));
    }

    [Test]
    public void APromiseThatFails_IsNothing()
    {
        var sent = new DataPackage();
        sent.SetDeferred(DataFormats.Text, () => throw new InvalidOperationException());
        Put(sent);

        Assert.That(new MacOSClipboard(_pasteboard).Read().Contains(DataFormats.Text), Is.False);
    }

    // Put on the clipboard, nothing is left to be made: whoever made it may be gone.
    [Test]
    public void OnTheClipboard_APromiseIsMadeAtOnce()
    {
        var sent = new DataPackage();
        var made = 0;
        sent.SetDeferred(DataFormats.Text, () => $"{++made}");
        new MacOSClipboard(_pasteboard).SetData(sent);

        Assert.That(made, Is.EqualTo(1));
    }

    // The keys held narrow what the system offers: Option copies, Command leaves what the target takes it for.
    [TestCase(NSDragOperation.Copy, DragDropEffects.Copy)]
    [TestCase(NSDragOperation.Generic, DragDropEffects.Move)]
    [TestCase(NSDragOperation.Link, DragDropEffects.Link)]
    [TestCase(NSDragOperation.Copy | NSDragOperation.Move, DragDropEffects.Copy | DragDropEffects.Move)]
    [TestCase(NSDragOperation.Private, DragDropEffects.None)]
    public void WhatTheSystemOffers_IsWhatMayBeDone(NSDragOperation offered, DragDropEffects effects) =>
        Assert.That(MacOSDragDrop.Effects(offered), Is.EqualTo(effects));

    [TestCase(DragDropEffects.Move, NSDragOperation.Generic, NSDragOperation.Generic)]
    [TestCase(DragDropEffects.Move, NSDragOperation.Move | NSDragOperation.Generic, NSDragOperation.Move)]
    [TestCase(DragDropEffects.Copy, NSDragOperation.Move, NSDragOperation.None)]
    [TestCase(DragDropEffects.Copy, (NSDragOperation)ulong.MaxValue, NSDragOperation.Copy)]
    [TestCase(DragDropEffects.None, (NSDragOperation)ulong.MaxValue, NSDragOperation.None)]
    public void TheAnswer_IsOneOfWhatIsOffered(DragDropEffects effects, NSDragOperation offered, NSDragOperation answer) =>
        Assert.That(MacOSDragDrop.Operation(effects, offered), Is.EqualTo(answer));

    private void Put(DataPackage package)
    {
        var items = MacOSClipboard.CreateItems(package, keepsPromises: true);
        NSPasteboard.Write(_pasteboard, items);
        foreach (var item in items)
            NSObject.Release(item);
    }
}
