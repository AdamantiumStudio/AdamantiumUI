using Adamantium.UI.Automation;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>How a driven application is started when nothing else is said.</summary>
[TestFixture]
public class LaunchOptionsTests
{
    // Where nobody sees it, unless a run asks for the desktop: no launch can forget it and show where someone works.
    [Test]
    public void AnApplication_RunsWhereNobodySeesIt_UnlessToldOtherwise()
    {
        Assert.That(new LaunchOptions().VirtualDisplay, Is.True);
    }
}
