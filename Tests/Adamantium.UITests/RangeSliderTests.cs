using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Primitives;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// What a two-bound control must never allow, and what it must survive. No template here - these are the value rules,
/// which is exactly where the control differs from a pair of Sliders.
/// </summary>
[TestFixture]
public class RangeSliderTests
{
    private static RangeSlider NewSlider() => new() { Minimum = 0, Maximum = 100, LowerValue = 20, UpperValue = 70 };

    // The value badges are the slider's own but not in its visual tree, so the discard of the tree did not reach them:
    // they stayed alive, following the theme, and held the slider and its page.
    [Test]
    public void ADiscardedRangeSlider_DiscardsItsValueBadges()
    {
        var slider = NewSlider();
        slider.IsValueToolTipEnabled = true;
        slider.Width = 200;
        var set = new Adamantium.UI.Themes.MacOsTheme.MacOsRangeSliderStyleSet();
        set.Initialize(null);
        foreach (var style in set.Styles)
        {
            style.Attach(slider);
        }

        var root = new Adamantium.UI.Controls.Decorators.Border { Width = 400, Height = 100, Child = slider };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        var thumb = ((RangeTrack)slider.GetTemplateChild("PART_Track")).LowerThumb;
        thumb.RaiseEvent(new DragStartedEventArgs(Adamantium.Mathematics.Vector2.Zero) { RoutedEvent = Thumb.DragStartedEvent });
        var badges = new System.Collections.Generic.List<Popup>();
        foreach (var child in slider.LogicalChildren)
        {
            if (child is Popup popup)
            {
                badges.Add(popup);
            }
        }

        Assume.That(badges, Has.Count.EqualTo(2), "a drag shows both badges");

        Adamantium.UI.Core.DiscardedVisuals.PublishSubtree(root);
        while (Adamantium.UI.Core.DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(badges.TrueForAll(badge => badge.IsDiscarded), Is.True);
    }

    // Each bound's thumb takes the keyboard in every theme; the keys move the bound whose thumb has it, as far as the
    // other bound lets it.
    [TestCase(typeof(Adamantium.UI.Themes.FluentTheme.RangeSliderStyleSet))]
    [TestCase(typeof(Adamantium.UI.Themes.GraphiteTheme.RangeSliderStyleSet))]
    [TestCase(typeof(Adamantium.UI.Themes.MacOsTheme.MacOsRangeSliderStyleSet))]
    public void TheKeys_MoveTheBoundWhoseThumbHasThem(System.Type styleSet)
    {
        var slider = NewSlider();
        slider.SmallChange = 1;
        slider.LargeChange = 10;
        slider.Width = 200;
        var set = (Adamantium.UI.Core.Resources.StyleSet)System.Activator.CreateInstance(styleSet);
        set.Initialize(null);
        foreach (var style in set.Styles)
        {
            style.Attach(slider);
        }

        var root = new Adamantium.UI.Controls.Decorators.Border { Width = 400, Height = 100, Child = slider };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        var track = (RangeTrack)slider.GetTemplateChild("PART_Track");

        void Press(Thumb thumb, Adamantium.UI.Core.Input.Key key) =>
            thumb.RaiseEvent(new Adamantium.UI.Core.Input.KeyEventArgs(Adamantium.UI.Core.Input.KeyboardDevice.CurrentDevice,
                key, Adamantium.UI.Core.Input.InputModifiers.None, 0)
            {
                RoutedEvent = Adamantium.UI.Core.Input.Keyboard.KeyDownEvent
            });

        Press(track.LowerThumb, Adamantium.UI.Core.Input.Key.RightArrow);
        var raised = slider.LowerValue;
        Press(track.LowerThumb, Adamantium.UI.Core.Input.Key.PageDown);
        var paged = slider.LowerValue;
        Press(track.UpperThumb, Adamantium.UI.Core.Input.Key.Home);
        var stopped = (slider.LowerValue, slider.UpperValue);
        Press(track.UpperThumb, Adamantium.UI.Core.Input.Key.End);
        Press(track.LowerThumb, Adamantium.UI.Core.Input.Key.Home);

        Assert.Multiple(() =>
        {
            Assert.That(track.LowerThumb.Focusable && track.UpperThumb.Focusable, Is.True);
            Assert.That(raised, Is.EqualTo(21));
            Assert.That(paged, Is.EqualTo(11));
            Assert.That(stopped, Is.EqualTo((11.0, 11.0)), "the end stops at the start");
            Assert.That((slider.LowerValue, slider.UpperValue), Is.EqualTo((0.0, 100.0)));
        });
    }

    // A key takes a bound only as far as the other lets it - and asks no further: the other bound moving later does not
    // drag it along. With ticks, a step is a tick at least, so a smaller one does not snap back where it was.
    [Test]
    public void AKey_TakesABoundOnlyAsFarAsTheOtherLetsIt()
    {
        var slider = NewSlider();
        slider.Width = 200;
        var set = new Adamantium.UI.Themes.MacOsTheme.MacOsRangeSliderStyleSet();
        set.Initialize(null);
        foreach (var style in set.Styles)
        {
            style.Attach(slider);
        }

        var root = new Adamantium.UI.Controls.Decorators.Border { Width = 400, Height = 100, Child = slider };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        var track = (RangeTrack)slider.GetTemplateChild("PART_Track");

        void Press(Thumb thumb, Adamantium.UI.Core.Input.Key key) =>
            thumb.RaiseEvent(new Adamantium.UI.Core.Input.KeyEventArgs(Adamantium.UI.Core.Input.KeyboardDevice.CurrentDevice,
                key, Adamantium.UI.Core.Input.InputModifiers.None, 0)
            {
                RoutedEvent = Adamantium.UI.Core.Input.Keyboard.KeyDownEvent
            });

        Press(track.UpperThumb, Adamantium.UI.Core.Input.Key.Home);
        var met = (slider.LowerValue, slider.UpperValue);
        Press(track.LowerThumb, Adamantium.UI.Core.Input.Key.LeftArrow);
        var lowered = (slider.LowerValue, slider.UpperValue);
        slider.IsSnapToTickEnabled = true;
        slider.TickFrequency = 5;
        Press(track.UpperThumb, Adamantium.UI.Core.Input.Key.RightArrow);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.EqualTo((20.0, 20.0)));
            Assert.That(lowered, Is.EqualTo((19.0, 20.0)), "the end stays where the keys put it");
            Assert.That(slider.UpperValue, Is.EqualTo(25), "a tick at least");
        });
    }

    [Test]
    public void TheBoundsCannotCross()
    {
        var slider = NewSlider();

        slider.LowerValue = 90;

        Assert.That(slider.LowerValue, Is.EqualTo(70), "the start cannot pass the end");
        Assert.That(slider.UpperValue, Is.EqualTo(70), "and the end stays where it was");
    }

    [Test]
    public void MinimumRangeKeepsTheSpanFromCollapsing()
    {
        var slider = NewSlider();
        slider.MinimumRange = 10;

        slider.LowerValue = 68;

        Assert.That(slider.LowerValue, Is.EqualTo(60), "kept ten units clear of the end");
    }

    /// <summary>
    /// Bindings arrive one at a time. A start that has to be squeezed because the end is still at its default must come
    /// back once the end arrives - otherwise a control bound to 20..70 settles on something else entirely, which is what
    /// it used to do (it read 1..70, because 20 was clamped against the default upper bound of 1 and then forgotten).
    /// </summary>
    [Test]
    public void AValueSqueezedByTheOtherBoundIsRestoredWhenThereIsRoom()
    {
        var slider = new RangeSlider();   // defaults: 0..1, so 20 does not fit yet

        slider.Maximum = 100;
        slider.LowerValue = 20;           // squeezed to 1 by the still-default UpperValue
        slider.UpperValue = 70;

        Assert.That(slider.LowerValue, Is.EqualTo(20));
        Assert.That(slider.UpperValue, Is.EqualTo(70));
    }

    [Test]
    public void MovingTheBoundsPullsTheSelectionInside()
    {
        var slider = NewSlider();

        slider.Maximum = 50;

        Assert.That(slider.UpperValue, Is.EqualTo(50), "the end follows the new ceiling");
        Assert.That(slider.LowerValue, Is.EqualTo(20), "the start was already inside");
    }

    [Test]
    public void RangeChangedFiresForEitherBound()
    {
        var slider = NewSlider();
        var fired = 0;
        slider.RangeChanged += (_, _) => fired++;

        slider.LowerValue = 30;
        slider.UpperValue = 60;

        Assert.That(fired, Is.EqualTo(2));
    }

    /// <summary>The whole point of splitting the base: bounds live one level up, so both kinds of range control share
    /// them and a Slider still gets its single Value.</summary>
    [Test]
    public void BothKindsOfRangeControlShareTheSameBounds()
    {
        Assert.That(typeof(RangeSlider), Is.InstanceOf<object>());
        Assert.That(typeof(RangeLimitsBase).IsAssignableFrom(typeof(RangeSlider)), Is.True);
        Assert.That(typeof(RangeLimitsBase).IsAssignableFrom(typeof(RangeBase)), Is.True);
        Assert.That(typeof(RangeBase).IsAssignableFrom(typeof(Slider)), Is.True);
        Assert.That(typeof(RangeBase).IsAssignableFrom(typeof(RingProgressBar)), Is.True);
    }
}
