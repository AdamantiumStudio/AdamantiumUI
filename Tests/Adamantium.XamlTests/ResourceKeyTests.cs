using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The build names every key a markup file declares to the assembly, with the type of what it holds - the
/// keyed entries of a dictionary and of a blueprint's resources, the colors of a theme variant's palette - so tools can
/// offer keys from an assembly whose markup they cannot see.</summary>
[TestFixture]
public class ResourceKeyTests
{
    private const string Namespaces = "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private static Assembly Build() => AumlCodegenHarness.Project(new Dictionary<string, string>
    {
        ["Icons.auml"] =
            $"<ResourceDictionary {Namespaces}>" +
            "<DrawingImage x:Key=\"SaveIcon\"><GeometryDrawing Geometry=\"M0,0 L1,1\"/></DrawingImage>" +
            "<SolidColorBrush x:Key=\"Ink\" Color=\"Red\"/>" +
            "</ResourceDictionary>",
        ["Night.auml"] =
            $"<ThemeVariantDefinition {Namespaces} Key=\"Night\"><ThemeVariantDefinition.Colors>" +
            "<PaletteColor Key=\"Ground\" Color=\"#101010\"/>" +
            "<PaletteColor Key=\"Fade\" Color=\"#80101010\" As=\"Color\"/>" +
            "</ThemeVariantDefinition.Colors></ThemeVariantDefinition>",
    }).Load();

    [Test]
    public void ADictionarysKeys_AreNamedWithTheTypesTheyHold() =>
        Assert.That(Keys(Build(), "Test.App.Icons"), Is.EquivalentTo(new[]
        {
            ("SaveIcon", typeof(DrawingImage)),
            ("Ink", typeof(SolidColorBrush)),
        }));

    [Test]
    public void APalettesColors_AreNamedAsBrushesOrAsColors() =>
        Assert.That(Keys(Build(), "Test.App.Night"), Is.EquivalentTo(new[]
        {
            ("Ground", typeof(SolidColorBrush)),
            ("Fade", typeof(Color)),
        }));

    private static IEnumerable<(string, System.Type)> Keys(Assembly assembly, string type) =>
        assembly.GetType(type).GetCustomAttributes<ResourceKeyAttribute>().Select(a => (a.Key, a.ValueType));
}
