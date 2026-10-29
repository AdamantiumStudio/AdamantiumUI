using System;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A pane opened by a docking region shows the icon its view model names by key, found among the resources.
/// </summary>
[TestFixture]
public class DockingPaneIconTests
{
    private FakeApp _app;
    private ResourceManager _resources;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer());
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void FreshResources()
    {
        _resources = new ResourceManager();
        _app.ResourceManager = _resources;
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
    }

    private sealed class Report : IDockablePane
    {
        public string PaneId => "report";
        public string PaneTitle => "Report";
        public string PaneIcon => "ReportIcon";
    }

    private sealed class Notes : IDockablePane
    {
        public string PaneId => "notes";
        public string PaneTitle => "Notes";
    }

    private sealed class NoResolver : IDependencyResolver
    {
        public T Resolve<T>(string name = "") => throw new NotSupportedException();
        public object Resolve(Type type, string name = "") => throw new NotSupportedException();
    }

    [Test]
    public void ATabShowsTheIconItsViewModelNames_AndOneNamingNoneHasNone()
    {
        var icon = new DrawingImage();
        var icons = new ResourceDictionary();
        icons.Add("ReportIcon", icon);
        _resources.AddSource(new Border(), icons, ResourceScope.Global);

        var area = new DockingArea { DividerThickness = 0 };
        var documents = new PaneGroup { Name = "documents", Zone = DockZone.Center };
        documents.Items.Add(new Pane { Header = "scene", Id = "scene" });
        area.Children.Add(documents);
        area.Measure(new Size<double>(1000, 800));
        area.Arrange(new Rect(0, 0, 1000, 800));
        var region = new Adamantium.Navigation.Region("docking", new NoResolver(), null);
        new DockingAreaRegionAdapter(new ViewLocator()).Attach(region, area);

        region.Add(new Report());
        region.Add(new Notes());
        var named = area.PaneById("report");
        var unnamed = area.PaneById("notes");

        Assert.Multiple(() =>
        {
            Assert.That(named.Icon, Is.SameAs(icon), "the picture its key names");
            Assert.That(named.HasIcon, Is.True);
            Assert.That(unnamed.Icon, Is.Null);
            Assert.That(unnamed.HasIcon, Is.False, "no room kept for an icon it has not");
        });
    }
}
