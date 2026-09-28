using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.UITests;

// A theme swap detaches the outgoing styles before applying the incoming ones: marker setters ({Binding},
// {ThemeResource}...) are undone by (component, property), not by style.
[TestFixture]
public class ThemeSwapStyleOrderTests
{
    private sealed class Source
    {
        public double Value { get; init; }
    }

    private static Style WidthFrom(Source source)
    {
        var style = new Style();
        style.Selector.Types.Add(typeof(Border));
        style.Setters.Add(new Setter(nameof(Border.Width), new Binding(nameof(Source.Value)) { Source = source }));
        return style;
    }

    private static Style WidthOf(double width)
    {
        var style = new Style();
        style.Selector.Types.Add(typeof(Border));
        style.Setters.Add(new Setter(nameof(Border.Width), width));
        return style;
    }

    // Plain values: removing the first of two styles on one property leaves the second's value (removals need not be LIFO).
    [Test]
    public void RemovingTheFIRSTOfTwoContributions_LeavesTheOtherOnesValue()
    {
        var border = new Border();
        var outgoing = WidthOf(40);
        var incoming = WidthOf(90);

        outgoing.Attach(border);
        incoming.Attach(border);
        Assert.That(border.Width, Is.EqualTo(90), "the last style applied is the one in force");

        outgoing.Detach(border);

        Assert.That(border.Width, Is.EqualTo(90),
            "taking away a style that was NOT the one in force must not disturb the one that is");
    }

    /// <summary>And the other direction, which always worked: removing the style that IS in force falls back to the one
    /// underneath it, rather than to nothing.</summary>
    [Test]
    public void RemovingTheONEInForce_FallsBackToTheOtherContribution()
    {
        var border = new Border();
        var under = WidthOf(40);
        var over = WidthOf(90);

        under.Attach(border);
        over.Attach(border);
        over.Detach(border);

        Assert.That(border.Width, Is.EqualTo(40));
    }

    /// <summary>The swap as it happens: the outgoing theme's styles are detached, the incoming theme's applied. What the
    /// control shows afterwards is the incoming theme's - whichever order the two halves ran in.</summary>
    [Test]
    public void DetachingTheOutgoingStyle_LeavesTheIncomingOnesValue()
    {
        var border = new Border();
        var outgoing = WidthFrom(new Source { Value = 40 });
        var incoming = WidthFrom(new Source { Value = 90 });

        outgoing.Attach(border);
        Assert.That(border.Width, Is.EqualTo(40), "the outgoing theme is what is on screen before the swap");

        incoming.Attach(border);
        outgoing.Detach(border);   // the old set goes away AFTER the new one landed

        Assert.That(border.Width, Is.EqualTo(90),
            "the incoming theme's value survives the outgoing one's teardown - a marker setter is undone by property, " +
            "not by style, so the old style's Remove would otherwise take the new style's value with it");
    }
}
