using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

// FocusManager.FocusMoved: every move of the focus told once it is over, wherever it went.
[TestFixture]
public class FocusMovedTests
{
    private readonly List<string> _told = [];
    private Border _from;
    private Border _to;
    private Border _further;
    private EventHandler<FocusMovedEventArgs> _onMoved;

    [SetUp]
    public void Listen()
    {
        FocusManager.Release(FocusManager.Focused);
        _from = new Border { Focusable = true };
        _to = new Border { Focusable = true };
        _further = new Border { Focusable = true };
        _onMoved = (_, e) => _told.Add($"moved {Name(e.OldFocus)}>{Name(e.NewFocus)} {e.NavigationMethod}");
        FocusManager.FocusMoved += _onMoved;
    }

    [TearDown]
    public void StopListening()
    {
        FocusManager.FocusMoved -= _onMoved;
        FocusManager.Release(FocusManager.Focused);
        _told.Clear();
    }

    [Test]
    public void AMove_IsToldAfterTheElements_WithTheKeysGoingToTheNewOne()
    {
        IInputComponent keysGoTo = null;
        EventHandler<FocusMovedEventArgs> onKeys = (_, _) => keysGoTo = KeyboardDevice.CurrentDevice.FocusedComponent;
        _from.LostFocus += (_, _) => _told.Add("lost");
        _to.GotFocus += (_, _) => _told.Add("got");

        FocusManager.Focus(_from, NavigationMethod.Mouse);
        FocusManager.FocusMoved += onKeys;
        try
        {
            FocusManager.Focus(_to, NavigationMethod.Tab);
            FocusManager.Focus(_to, NavigationMethod.Tab);
        }
        finally
        {
            FocusManager.FocusMoved -= onKeys;
        }

        Assert.That(_told, Is.EqualTo(new[] { "moved none>from Mouse", "lost", "got", "moved from>to Tab" }));
        Assert.That(keysGoTo, Is.SameAs(_to));
    }

    [Test]
    public void TheFocusLetGo_IsToldAsGoingToNone_OnceItIsGone()
    {
        IInputComponent focusedWhenTold = _from;
        EventHandler<FocusMovedEventArgs> onFocused = (_, _) => focusedWhenTold = FocusManager.Focused;
        FocusManager.Focus(_from);
        FocusManager.FocusMoved += onFocused;
        try
        {
            FocusManager.Release(_from);
        }
        finally
        {
            FocusManager.FocusMoved -= onFocused;
        }

        Assert.That(_told[^1], Is.EqualTo("moved from>none Unspecified"));
        Assert.That(focusedWhenTold, Is.Null);
    }

    [Test]
    public void TheWindowLeft_IsToldWhileItIsBeingLeft()
    {
        var leaving = false;
        EventHandler<FocusMovedEventArgs> onLeaving = (_, e) => leaving = e.NewFocus == null && FocusManager.IsLeavingWindow;
        FocusManager.Focus(_from);
        FocusManager.FocusMoved += onLeaving;
        try
        {
            FocusManager.LeaveWindow(_from);
        }
        finally
        {
            FocusManager.FocusMoved -= onLeaving;
        }

        Assert.That(leaving, Is.True, "The focus comes back when the window is active again.");
    }

    [Test]
    public void AMoveAnElementMakesOnAgain_IsToldOnce()
    {
        _to.GotFocus += (_, _) => FocusManager.Focus(_further);

        FocusManager.Focus(_to);

        Assert.That(_told, Is.EqualTo(new[] { "moved to>further Unspecified" }),
            "The move to the element that passed the focus on is over before it is told.");
    }

    private string Name(IInputComponent element) =>
        element == null ? "none"
        : ReferenceEquals(element, _from) ? "from"
        : ReferenceEquals(element, _to) ? "to"
        : ReferenceEquals(element, _further) ? "further"
        : "other";
}
