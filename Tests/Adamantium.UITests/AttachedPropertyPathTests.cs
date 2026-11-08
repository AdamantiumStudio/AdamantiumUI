using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

// A library's style names its own attached property with the prefix its markup maps to the library's namespace -
// local:WebViewEngines.UseApplicationEngine. The setter was given that name as it is, and the owner was looked for by
// "local:WebViewEngines", which no type is called: the setter set nothing.
[TestFixture]
public class AttachedPropertyPathTests
{
    [Test]
    public void AnOwnerWithItsMarkupPrefix_IsFoundAsWithout()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AdamantiumPropertyMap.ResolveProperty(typeof(Button), "Grid.Row"), Is.SameAs(Grid.RowProperty));
            Assert.That(AdamantiumPropertyMap.ResolveProperty(typeof(Button), "local:Grid.Row"), Is.SameAs(Grid.RowProperty));
            Assert.That(AdamantiumPropertyMap.ResolveProperty(typeof(Button), "(local:Grid.Row)"), Is.SameAs(Grid.RowProperty));
        });
    }
}
