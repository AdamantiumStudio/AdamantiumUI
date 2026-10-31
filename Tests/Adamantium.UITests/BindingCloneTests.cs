using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Localization;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A binding written once and set on many targets is cloned for each - a torn-off tab's window title, an item
/// container's style - and the copy has to carry every setting the original was written with.</summary>
[TestFixture]
public class BindingCloneTests
{
    [Test]
    public void ABindingsClone_KeepsItsImmediacy()
    {
        var clone = (Binding)new Binding("Name") { IsImmediate = true }.Clone();

        Assert.That(clone.IsImmediate, Is.True);
    }

    [Test]
    public void AMultiBindingsClone_KeepsItsImmediacy()
    {
        var clone = (MultiBinding)new MultiBinding { IsImmediate = true }.Clone();

        Assert.That(clone.IsImmediate, Is.True);
    }

    [Test]
    public void ALocalizesClone_KeepsItsFormat()
    {
        var clone = (Localize)new Localize { StringFormat = "[{0}]" }.Clone();

        Assert.That(clone.StringFormat, Is.EqualTo("[{0}]"));
    }
}
