using Adamantium.UI.Platforms.Linux;
using NUnit.Framework;

namespace Adamantium.UITests;

public class DesktopPortalSettingsTests
{
    [TestCase("Adwaita Sans 11", "Adwaita Sans")]
    [TestCase("Cantarell 11", "Cantarell")]
    [TestCase("Noto Sans Semi-Bold Italic 10.5", "Noto Sans")]
    [TestCase("Ubuntu, 12", "Ubuntu")]
    [TestCase("Inter 14px", "Inter")]
    [TestCase("Bold", "Bold")]
    public void APangoDescription_GivesItsFamily(string description, string family) =>
        Assert.That(DesktopPortalSettings.FamilyOf(description), Is.EqualTo(family));
}
