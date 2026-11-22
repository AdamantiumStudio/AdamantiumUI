using System.Linq;
using Adamantium.UI;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The application and its interface agree: whatever <see cref="IUIApplication"/> offers, the application does
/// itself - a member left to the interface's default does nothing, so code holding the interface would get nothing.
/// </summary>
[TestFixture]
public class UIApplicationInterfaceTests
{
    [Test]
    public void EveryMemberOfTheInterface_IsTheApplicationsOwn()
    {
        var map = typeof(UIApplication).GetInterfaceMap(typeof(IUIApplication));
        var leftToTheInterface = map.TargetMethods
            .Where(method => method.DeclaringType == typeof(IUIApplication))
            .Select(method => method.Name)
            .ToList();

        Assert.That(leftToTheInterface, Is.Empty);
    }
}
