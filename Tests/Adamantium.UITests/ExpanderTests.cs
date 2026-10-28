using System;
using System.ComponentModel;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The Expander's own logic - what it does regardless of how a theme dresses it: folding, the events that
/// announce it, the keys, and the refusal to fold a section that must stay open.</summary>
[TestFixture]
public class ExpanderTests
{
    [Test]
    public void ItStartsFoldedAndTogglesBothWays()
    {
        var expander = new Expander { Header = "Transform" };

        Assert.That(expander.IsExpanded, Is.False, "a section opens folded - a page of them must not come up unrolled");

        Assert.That(expander.Toggle(), Is.True);
        Assert.That(expander.IsExpanded, Is.True);

        Assert.That(expander.Toggle(), Is.True);
        Assert.That(expander.IsExpanded, Is.False);
    }

    [Test]
    public void EachChangeIsAnnouncedOnce()
    {
        var expander = new Expander();
        int expanded = 0, collapsed = 0;
        expander.Expanded += (_, _) => expanded++;
        expander.Collapsed += (_, _) => collapsed++;

        expander.IsExpanded = true;
        expander.IsExpanded = true;   // same value: nothing happened, so nothing is announced
        expander.IsExpanded = false;

        Assert.Multiple(() =>
        {
            Assert.That(expanded, Is.EqualTo(1));
            Assert.That(collapsed, Is.EqualTo(1));
        });
    }

    // A section that must stay open still wants its header - and must not be foldable by any route, including the
    // keyboard. Opening one is always allowed: CanCollapse says "cannot be closed", not "cannot be touched".
    [Test]
    public void ASectionThatCannotCollapseRefusesToClose()
    {
        var expander = new Expander { CanCollapse = false };

        Assert.That(expander.Toggle(), Is.True, "still opens");
        Assert.That(expander.IsExpanded, Is.True);

        Assert.That(expander.Toggle(), Is.False, "and refuses to close again");
        Assert.That(expander.IsExpanded, Is.True);
    }

    [Test]
    [TestCase(Key.Space)]
    [TestCase(Key.Enter)]
    public void SpaceAndEnterFoldIt(Key key)
    {
        var expander = new Expander();
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };

        expander.RaiseEvent(args);

        Assert.Multiple(() =>
        {
            Assert.That(expander.IsExpanded, Is.True);
            Assert.That(args.Handled, Is.True, "claimed, so the key does not travel on to a parent");
        });
    }

    [Test]
    public void ADisabledExpanderIgnoresTheKeyboard()
    {
        var expander = new Expander { IsEnabled = false };
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.Space, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };

        expander.RaiseEvent(args);

        Assert.That(expander.IsExpanded, Is.False);
    }

    [Test]
    public void ItIsAKeyboardStop()
    {
        Assert.That(new Expander().Focusable, Is.True, "a header that answers Space has to be reachable by Tab");
    }

    // A key event travels UP from whatever had the keyboard, so every space typed into a field inside a section reached
    // the section's own header - and folded the section away in the middle of a word.
    [Test]
    [TestCase(Key.Space)]
    [TestCase(Key.Enter)]
    public void AKeyFromInsideTheContentIsNotTheHeadersKey(Key key)
    {
        var box = new TextBox();
        var expander = new Expander { Content = box };
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            OriginalSource = box
        };

        expander.RaiseEvent(args);

        Assert.Multiple(() =>
        {
            Assert.That(expander.IsExpanded, Is.False, "typing in a field does not fold the section it is in");
            Assert.That(args.Handled, Is.False, "and the key is left for whoever is typing");
        });
    }

    private sealed class Fold : INotifyPropertyChanged
    {
        private bool _open;

        public bool Open
        {
            get => _open;
            set
            {
                _open = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Open)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    // Folding state belongs in a view-model: which groups an inspector has open is the user's, and it has to survive
    // the panel being rebuilt. The binding is two-way by default, so the header carries the state BOTH ways - and it
    // has to keep carrying it after the user has clicked, which is what a Local value written by the click would end.
    [Test]
    public void ItsFoldingStateBindsBothWays()
    {
        var model = new Fold();
        var expander = new Expander { DataContext = model };
        expander.SetBinding("IsExpanded", new Binding("Open"));

        Assert.That(expander.IsExpanded, Is.False, "starts where the model is");

        // Source changes are batched and applied on the frame's flush - see BindingBatchingTests.
        model.Open = true;
        BindingUpdateQueue.Flush();
        Assert.That(expander.IsExpanded, Is.True, "the model opens it");

        expander.Toggle();
        Assert.That(model.Open, Is.False, "and a click writes back");

        model.Open = true;
        BindingUpdateQueue.Flush();
        Assert.That(expander.IsExpanded, Is.True, "the model still moves it AFTER a click - the binding survived");
    }

    [Test]
    public void OnScreen_TheContentUnfoldsAndFoldsAwayOverItsDuration_AndTheChevronTurns()
    {
        var (expander, reveal, turn) = OnScreen(TimeSpan.FromSeconds(1));

        expander.IsExpanded = true;
        Assert.That(reveal.Visibility, Is.EqualTo(Visibility.Visible), "it is there to unfold");
        AnimationManager.Tick(0.5);
        Assert.Multiple(() =>
        {
            Assert.That(reveal.Progress, Is.InRange(0.01, 0.99), "half way, part of the content shows");
            Assert.That(turn.RotationAngle, Is.InRange(1, 89), "and the chevron is turning");
        });
        AnimationManager.Tick(0.6);
        Assert.Multiple(() =>
        {
            Assert.That(reveal.Progress, Is.EqualTo(1));
            Assert.That(turn.RotationAngle, Is.EqualTo(90), "opening down, it points down");
        });

        expander.IsExpanded = false;
        AnimationManager.Tick(0.5);
        Assert.That(reveal.Visibility, Is.EqualTo(Visibility.Visible), "still folding away");
        AnimationManager.Tick(0.6);
        Assert.Multiple(() =>
        {
            Assert.That(reveal.Progress, Is.EqualTo(0));
            Assert.That(reveal.Visibility, Is.EqualTo(Visibility.Collapsed), "folded, it is not measured");
            Assert.That(turn.RotationAngle, Is.EqualTo(0));
        });
    }

    [Test]
    public void TakenOutMidFold_ItComesBackFolded_NotHalfWay()
    {
        var (expander, reveal, turn) = OnScreen(TimeSpan.FromSeconds(1));
        var root = (Rendering.TestRoot)expander.VisualParent;
        expander.IsExpanded = true;
        AnimationManager.Tick(1.1);

        expander.IsExpanded = false;
        AnimationManager.Tick(0.4);
        root.Remove(expander);
        AnimationManager.CancelSubtree(expander);
        root.Add(expander);

        Assert.Multiple(() =>
        {
            Assert.That(reveal.Progress, Is.EqualTo(0));
            Assert.That(reveal.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(turn.RotationAngle, Is.EqualTo(0));
        });
    }

    [Test]
    public void BeforeItIsLaidOut_AChangeShowsAtOnce()
    {
        var (expander, reveal, _) = Templated(TimeSpan.FromSeconds(1));
        new Rendering.TestRoot(400, 400).Add(expander);

        expander.IsExpanded = true;

        Assert.That(reveal.Progress, Is.EqualTo(1), "a state bound in as the page comes up does not play");
    }

    [Test]
    public void WithNoDuration_OrNotOnScreen_TheChangeShowsAtOnce()
    {
        var (instant, instantReveal, _) = OnScreen(TimeSpan.Zero);
        var (offScreen, offScreenReveal, turn) = Templated(TimeSpan.FromSeconds(1));

        instant.IsExpanded = true;
        offScreenReveal.Direction = ExpandDirection.Right;
        offScreen.IsExpanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(instantReveal.Progress, Is.EqualTo(1));
            Assert.That(offScreenReveal.Progress, Is.EqualTo(1));
            Assert.That(offScreenReveal.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(turn.RotationAngle, Is.EqualTo(180), "opening right, it points back at the header");
        });
    }

    private static (Expander Expander, RevealDecorator Reveal, Transform Turn) OnScreen(TimeSpan duration)
    {
        var templated = Templated(duration);
        new Rendering.TestRoot(400, 400).Add(templated.Expander);
        templated.Expander.Measure(new Size<double>(400, 400));
        templated.Expander.Arrange(new Rect(0, 0, 400, 400));
        return templated;
    }

    private static (Expander Expander, RevealDecorator Reveal, Transform Turn) Templated(TimeSpan duration)
    {
        var reveal = new RevealDecorator { Child = new Border { Height = 100 } };
        var turn = new Transform();
        var expander = new Expander
        {
            ExpandDuration = duration,
            Template = new ControlTemplate(() =>
            {
                var header = new Border { Height = 20 };
                var result = new TemplateResult { RootComponent = new StackPanel { Children = { header, reveal } } };
                result.RegisterName("PART_Header", header);
                result.RegisterName("PART_Reveal", reveal);
                result.RegisterName("PART_GlyphTurn", turn);
                return result;
            })
        };
        return (expander, reveal, turn);
    }
}
