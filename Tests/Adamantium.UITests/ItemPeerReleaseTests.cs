using System;
using System.Runtime.CompilerServices;
using Adamantium.UI.Controls;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>An items control's automation peer stands in for each item with no element of its own. An item gone from
/// the control has nothing to stand in for: kept, every item a list ever held stayed alive - a page rebuilding a list of
/// thousands added thousands of peers each time automation looked at it.</summary>
[TestFixture]
public class ItemPeerReleaseTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Show(ItemsControl list)
    {
        var item = new object();
        list.Items.Add(item);
        list.GetAutomationPeer().GetChildren();
        return new WeakReference(item);
    }

    [Test]
    public void AnItemGoneFromTheControl_IsNotKeptByItsPeer()
    {
        var list = new ListBox();
        var gone = Show(list);

        list.Items.Clear();
        list.GetAutomationPeer().GetChildren();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(gone.IsAlive, Is.False);
    }

    [Test]
    public void AnItemStillThere_KeepsItsPeer()
    {
        var list = new ListBox();
        list.Items.Add(new object());

        var first = list.GetAutomationPeer().GetChildren()[0];
        var second = list.GetAutomationPeer().GetChildren()[0];

        Assert.That(second, Is.SameAs(first));
    }
}
