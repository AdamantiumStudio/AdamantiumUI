using System;
using System.Collections.Generic;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// Setting a property by a name the element does not have used to do nothing at all, without a word. It still sets
/// nothing - but says so, once per kind of mistake. A STYLE reaching an element without the property is not a mistake: a
/// style matched by class lands on elements of every type, and passes over what they do not have, quietly - the way its
/// plain values always did, and its other values now do as well instead of throwing.
/// </summary>
[TestFixture]
public class PropertyTraceTests
{
    private List<string> _reports;
    private Action<string> _collect;

    [SetUp]
    public void Listen()
    {
        _reports = [];
        _collect = _reports.Add;
        PropertyTrace.Sink += _collect;
    }

    [TearDown]
    public void StopListening() => PropertyTrace.Sink -= _collect;

    private static Style ByClass(params Setter[] setters)
    {
        var style = new Style();
        style.Selector.Classes.Add("Wide");
        foreach (var setter in setters) style.Setters.Add(setter);
        return style;
    }

    [Test]
    public void ANameTheElementDoesNotHave_IsReported()
    {
        new Border().SetValue("Orientation", 1);

        Assert.That(_reports, Has.Some.Contains("Border").And.Contains("Orientation"));
    }

    [Test]
    public void ANameItHas_IsSetAndNotReported()
    {
        var border = new Border();

        border.SetValue("Background", Brushes.Red);

        Assert.Multiple(() =>
        {
            Assert.That(border.Background, Is.SameAs(Brushes.Red));
            Assert.That(_reports, Is.Empty);
        });
    }

    [Test]
    public void AStyleReachingAnElementWithoutTheProperty_PassesOverIt()
    {
        var border = new Border();
        border.Classes.Add("Wide");
        var style = ByClass(
            new Setter("Orientation", new PerTargetValue(() => 1)),
            new Setter("Orientation", new ResourceReference("Anything")),
            new Setter("Orientation", new ThemeResource("Anything")),
            new Setter("Orientation", new ObservableResource("Anything")),
            new Setter("Orientation", "Horizontal"));

        Assert.DoesNotThrow(() => style.Attach(border));
        Assert.DoesNotThrow(() => style.Detach(border));
        Assert.That(_reports, Is.Empty, "a class selector reaching an element without the property is no mistake");
    }

    [Test]
    public void ALiveResourceForANameTheElementDoesNotHave_IsReported()
    {
        var connected = new ThemeResource("AccentColor").Apply(new Border(), "Orientation");

        Assert.Multiple(() =>
        {
            Assert.That(connected, Is.Null, "nothing to connect to");
            Assert.That(_reports, Has.Some.Contains("Orientation").And.Contains("ThemeResource"));
        });
    }
}
