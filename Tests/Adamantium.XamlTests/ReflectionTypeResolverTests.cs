using Adamantium.UI.Markup.CodeGeneration.Reflection;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// The designer resolves markup by reflection, and a library the previewed project references is not loaded until code
// touches it. Markup that names it - clr-namespace:...;assembly=Lib - showed "Unknown type" for every control of it.
[TestFixture]
public class ReflectionTypeResolverTests
{
    [Test]
    public void AnAssemblyTheMarkupNamesButNothingLoaded_IsLoadedByName()
    {
        var resolver = new ReflectionTypeResolver([]);

        var assembly = resolver.GetResolvedAssembly("Adamantium.UI.Controls");

        Assert.That(assembly, Is.Not.Null);
        Assert.That(assembly.GetTypeByShortName("Border"), Is.Not.Null);
    }
}
