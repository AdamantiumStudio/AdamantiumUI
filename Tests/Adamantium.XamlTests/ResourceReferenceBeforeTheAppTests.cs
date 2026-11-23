using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A resource reference written before there is an application - a template built ahead of it, a test's
/// element - is looked up when its element joins the tree, among the application's resources as they are then.</summary>
[TestFixture]
public class ResourceReferenceBeforeTheAppTests
{
    [Test]
    public void AReferenceWrittenBeforeTheApp_IsResolvedWhenItsElementJoinsTheTree()
    {
        var current = typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current));
        var saved = current.GetValue(null);
        try
        {
            current.SetValue(null, null);
            var border = new Border();
            ResourceResolver.SetDeferred(border, nameof(Border.Background), "ArrivingFill");

            var fill = new SolidColorBrush(Colors.SeaGreen);
            var app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
            var window = new Window { Width = 100, Height = 100 };
            var resources = new ResourceDictionary();
            resources.Add("ArrivingFill", fill);
            app.ResourceManager.AddSource(window, resources, ResourceScope.Global);
            current.SetValue(null, app);
            window.Content = border;
            WindowExtension.UpdateTree(window);

            Assert.That(border.Background, Is.SameAs(fill));
        }
        finally
        {
            current.SetValue(null, saved);
        }
    }
}
