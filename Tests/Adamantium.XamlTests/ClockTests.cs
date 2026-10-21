using System;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>One clock, every style, in the theme's own template: hands at the angles of the time shown, turned by the
/// compositor while it is live, the face marked as its style says; figures as the language writes the time, lit
/// segments, flip cards; automation reads the time.</summary>
[TestFixture]
public class ClockTests
{
    private FakeApp _app;
    private Window _window;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void UseFluent()
    {
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    [TearDown]
    public void LetGo()
    {
        if (_window != null)
        {
            _window.Content = null;
            _window = null;
        }

        AnimationManager.Reset();
    }

    private Clock Shown(Clock clock)
    {
        clock.HorizontalAlignment = HorizontalAlignment.Left;
        clock.VerticalAlignment = VerticalAlignment.Top;
        _window = new Window { Width = 800, Height = 600, Content = clock };
        _window.ApplyCurrentTheme();
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }

        return clock;
    }

    private static Clock At(ClockStyle style, int hour, int minute, int second, string language = "ru-RU") => new()
    {
        ClockStyle = style, IsLive = false, Time = new DateTime(2026, 10, 19, hour, minute, second), Language = language,
    };

    [Test]
    public void TheHands_StandAtTheTimeShown()
    {
        var clock = Shown(At(ClockStyle.Classic, 3, 30, 15));

        Assert.That(clock.HourTurn.RotationAngle, Is.EqualTo(105.125).Within(0.01), "past half past three, by the seconds too");
        Assert.That(clock.MinuteTurn.RotationAngle, Is.EqualTo(181.5).Within(0.01));
        Assert.That(clock.SecondTurn.RotationAngle, Is.EqualTo(90).Within(0.01));
    }

    [Test]
    public void WithoutSeconds_ThereIsNoSecondHand()
    {
        var clock = At(ClockStyle.Classic, 3, 30, 15);
        clock.ShowSeconds = false;

        Assert.That(Shown(clock).SecondTurn, Is.Null);
    }

    [Test]
    public void ALiveClock_HasTheCompositorTurnItsHands()
    {
        var clock = Shown(new Clock { ClockStyle = ClockStyle.Classic });

        foreach (var turn in new[] { clock.HourTurn, clock.MinuteTurn, clock.SecondTurn })
        {
            Assert.That(Compositor.EntryFor(turn)?.Channel, Is.EqualTo(CompositorChannel.Transform));
        }
    }

    [Test]
    public void ALiveClock_StandsAtTheTimeNow_InItsZone()
    {
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var clock = Shown(new Clock { ClockStyle = ClockStyle.Classic, TimeZone = tokyo });
        var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, tokyo);

        Assert.That(clock.MinuteTurn.RotationAngle, Is.EqualTo(now.TimeOfDay.TotalSeconds % 3600 / 10).Within(1));
    }

    [Test]
    public void ALiveClockStopped_ShowsItsTime()
    {
        var clock = Shown(new Clock { ClockStyle = ClockStyle.Classic });

        clock.Time = new DateTime(2026, 10, 19, 3, 30, 15);
        clock.IsLive = false;

        Assert.That(clock.HourTurn.RotationAngle, Is.EqualTo(105.125).Within(0.01), "the turning hands let go");
        Assert.That(Compositor.EntryFor(clock.SecondTurn), Is.Null);
    }

    [Test]
    public void ADetachedClock_StopsTurning()
    {
        var clock = Shown(new Clock { ClockStyle = ClockStyle.Classic });
        var minute = clock.MinuteTurn;

        _window.Content = null;
        WindowExtension.UpdateTree(_window);

        Assert.That(Compositor.EntryFor(minute), Is.Null);
    }

    [TestCase(ClockStyle.Text)]
    [TestCase(ClockStyle.SevenSegment)]
    [TestCase(ClockStyle.Flip)]
    [TestCase(ClockStyle.Classic)]
    public void WithoutATime_ItShowsNone(ClockStyle style)
    {
        var clock = Shown(new Clock { ClockStyle = style, IsLive = false });

        Assert.That(clock.ShownText, Is.Empty);
        Assert.That(clock.Texts.Select(t => t.Text), Is.All.Empty);
        Assert.That(clock.Digits.SelectMany(d => d).Where(s => s.Opacity > 0.5), Is.Empty);
        Assert.That(clock.Cards.Select(c => ((Adamantium.UI.Controls.Text.TextBlock)c.Child).Text), Is.All.Empty);
        if (style == ClockStyle.Classic)
        {
            var hands = Descendants<RadialPanel>(clock)
                .Single(p => p.Children.OfType<Adamantium.UI.Controls.Decorators.Border>().Any(b => b.RenderTransform == clock.HourTurn));
            Assert.That(hands.Visibility, Is.EqualTo(Visibility.Hidden), "no hands");
        }
    }

    [Test]
    public void TwelveHourFigures_LeaveTheTensOfHoursDark()
    {
        var segments = Shown(At(ClockStyle.SevenSegment, 14, 5, 9, "en-US"));
        Assert.That(segments.Digits[0].Where(s => s.Opacity > 0.5), Is.Empty, "2 pm, not 02");

        var cards = Shown(At(ClockStyle.Flip, 14, 5, 9, "en-US"));
        Assert.That(cards.Cards.Select(c => ((Adamantium.UI.Controls.Text.TextBlock)c.Child).Text),
            Is.EqualTo(new[] { "", "2", "0", "5", "0", "9" }));
    }

    [Test]
    public void AFaceGivenAnOblong_StaysRound()
    {
        var clock = At(ClockStyle.Classic, 3, 0, 0);
        clock.Width = 200;
        clock.Height = 120;
        Shown(clock);

        var plate = (UIComponent)clock.Face.VisualChildren.First();
        Assert.That(plate.RenderSize.Width, Is.EqualTo(120).Within(0.5));
        Assert.That(plate.RenderSize.Height, Is.EqualTo(120).Within(0.5));
    }

    [Test]
    public void TheInnerRing_LightsItsOwnLabel()
    {
        var face = new ClockFace
        {
            FaceStyle = ClockFaceStyle.Labels,
            InnerLabels = Enumerable.Range(12, 12).Select(h => h.ToString()).ToArray(),
            ChosenLabel = 13,
        };
        _window = new Window { Width = 300, Height = 300, Content = face };
        _window.ApplyCurrentTheme();
        WindowExtension.UpdateTree(_window);

        var lit = Descendants<Adamantium.UI.Controls.Text.TextBlock>(face).Where(t => t.Classes.Contains("ClockFaceLabelChosen"));
        Assert.That(lit.Select(t => t.Text), Is.EqualTo(new[] { "13" }));
    }

    [TestCase(ClockStyle.Classic, "12")]
    [TestCase(ClockStyle.Roman, "XII")]
    public void TheFace_IsMarkedAsItsStyleSays(ClockStyle style, string top)
    {
        var clock = Shown(At(style, 3, 0, 0));

        Assert.That(clock.Face, Is.Not.Null);
        Assert.That(Descendants<Adamantium.UI.Controls.Text.TextBlock>(clock.Face).First().Text, Is.EqualTo(top));
    }

    [TestCase("ru-RU", "14", "05", "09")]
    [TestCase("en-US", "2", "05", "09 PM")]
    public void TextFigures_AreAsTheLanguageWritesTheTime(string language, string hours, string minutes, string seconds)
    {
        var clock = Shown(At(ClockStyle.Text, 14, 5, 9, language));

        Assert.That(clock.Texts.Select(t => t.Text.Replace('\u202F', ' ')), Is.EqualTo(new[] { hours, minutes, seconds }));
        Assert.That(clock.Texts[0].FontSize, Is.EqualTo(clock.FontSize), "figures as large as the clock asks");
    }

    [Test]
    public void TheMarkedPart_IsLitInEveryStyleOfFigures()
    {
        Clock Marked(ClockStyle style)
        {
            var clock = At(style, 18, 45, 0);
            clock.MarkedParts = DurationParts.Minutes;
            return Shown(clock);
        }

        var flip = Marked(ClockStyle.Flip);
        var text = Marked(ClockStyle.Text);
        var segments = Marked(ClockStyle.SevenSegment);

        Assert.Multiple(() =>
        {
            Assert.That(segments.Digits[2].Where(s => !s.Classes.Contains("ClockSegmentOff")),
                Is.All.Matches<Border>(s => s.Classes.Contains("ClockSegmentMarked")));
            Assert.That(segments.Digits[0].Any(s => s.Classes.Contains("ClockSegmentMarked")), Is.False);
            Assert.That(((TextBlock)flip.Cards[2].Child).Classes.Contains("ClockFlipFigureMarked"));
            Assert.That(((TextBlock)flip.Cards[0].Child).Classes.Contains("ClockFlipFigureMarked"), Is.False);
            Assert.That(text.Texts[1].Classes.Contains("ClockTextMarked"));
            Assert.That(text.Texts[0].Classes.Contains("ClockTextMarked"), Is.False);
        });

        segments.MarkedParts = DurationParts.None;
        Assert.That(segments.Digits.SelectMany(d => d).Any(s => s.Classes.Contains("ClockSegmentMarked")), Is.False);
    }

    [Test]
    public void SevenSegments_LightTheFigures()
    {
        var clock = Shown(At(ClockStyle.SevenSegment, 18, 0, 0));

        Assert.That(clock.Digits, Has.Count.EqualTo(6));
        var one = clock.Digits[0];
        Assert.That(one.Select(s => s.Opacity > 0.5), Is.EqualTo(new[] { false, true, true, false, false, false, false }));
        var eight = clock.Digits[1];
        Assert.That(eight.All(s => s.Opacity > 0.5), "an 8 lights every segment");
    }

    [Test]
    public void AFigureThatChanges_LightsItsNewSegments()
    {
        var clock = Shown(At(ClockStyle.SevenSegment, 18, 0, 0));

        clock.Time = new DateTime(2026, 10, 19, 11, 0, 0);

        Assert.That(clock.Digits[1].Select(s => s.Opacity > 0.5),
            Is.EqualTo(new[] { false, true, true, false, false, false, false }), "the 8 that became a 1 lets its segments go");
    }

    [Test]
    public void AComponentThemedAlready_IsRestyledWhenItsClassesChange()
    {
        var border = new Adamantium.UI.Controls.Decorators.Border
        {
            Classes = Classes.Parse("ClockSegmentOff"), Width = 10, Height = 10,
        };
        _window = new Window { Width = 100, Height = 100, Content = border };
        _window.ApplyCurrentTheme();
        WindowExtension.UpdateTree(_window);
        Assert.That(border.Opacity, Is.LessThan(0.5));

        border.Classes = Classes.Parse("ClockSegmentOn");
        Assert.That(border.Opacity, Is.EqualTo(1));

        border.Classes = Classes.Parse("ClockSegmentOff");
        Assert.That(border.Opacity, Is.LessThan(0.5), "and back");
    }

    [Test]
    public void AnOwnClassStyle_ComesOffWithItsClass()
    {
        var wide = new Style();
        wide.Selector.Types.Add(typeof(Adamantium.UI.Controls.Decorators.Border));
        wide.Selector.Classes.Add("Wide");
        wide.Setters.Add(new Setter("Width", 90.0));
        var border = new Adamantium.UI.Controls.Decorators.Border { Classes = Classes.Parse("Wide") };
        border.Styles.Add(wide);
        _window = new Window { Width = 100, Height = 100, Content = border };
        _window.ApplyCurrentTheme();
        WindowExtension.UpdateTree(_window);
        Assert.That(border.Width, Is.EqualTo(90));

        border.Classes = Classes.Parse("Narrow");

        Assert.That(border.Width, Is.NaN, "the class taken away takes its style's width with it");
    }

    [Test]
    public void AClassChange_LeavesTheStylesThatStay()
    {
        var style = new Style();
        style.Selector.Types.Add(typeof(Button));
        var trigger = new Adamantium.UI.Core.Resources.Triggers.PropertyTrigger { Property = "IsEnabled", Value = false };
        trigger.Add(new Setter { Property = "Width", Value = "50" });
        style.Triggers.Add(trigger);
        var button = new Button { Classes = Classes.Parse("First") };
        button.Styles.Add(style);
        _window = new Window { Width = 100, Height = 100, Content = button };
        _window.ApplyCurrentTheme();
        WindowExtension.UpdateTree(_window);
        var staying = button.StyleActivators.ToList();

        button.Classes = Classes.Parse("Second");

        Assert.That(button.StyleActivators, Is.EquivalentTo(staying), "the button's triggers are not torn down and replayed");
    }

    [Test]
    public void FlipCards_ShowTheFigures()
    {
        var clock = Shown(At(ClockStyle.Flip, 9, 41, 0));

        Assert.That(clock.Cards.Select(c => ((Adamantium.UI.Controls.Text.TextBlock)c.Child).Text),
            Is.EqualTo(new[] { "0", "9", "4", "1", "0", "0" }));
    }

    [Test]
    public void Automation_ReadsTheTime()
    {
        var clock = Shown(At(ClockStyle.Roman, 14, 5, 9));
        var peer = (ClockAutomationPeer)clock.GetAutomationPeer();

        Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Text));
        Assert.That(peer.Value, Is.EqualTo("14:05:09"));
        Assert.That(peer.IsReadOnly);
    }

    [Test]
    public void ChangingTheStyle_RebuildsTheClock()
    {
        var clock = Shown(At(ClockStyle.Classic, 9, 41, 0));

        clock.ClockStyle = ClockStyle.Flip;

        Assert.That(clock.Face, Is.Null);
        Assert.That(clock.Cards, Has.Count.EqualTo(6));
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(IUIComponent root) where T : class
    {
        foreach (var child in root.VisualChildren)
        {
            if (child is T found)
            {
                yield return found;
            }

            foreach (var deeper in Descendants<T>(child))
            {
                yield return deeper;
            }
        }
    }
}
