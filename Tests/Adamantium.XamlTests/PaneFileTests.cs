using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Text;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A file whose root is <c>&lt;Pane&gt;</c> is a class of its own, the way a <c>&lt;View&gt;</c> file is: a docking pane
/// written once - title, kind, where it goes, its content - and placed by name.
/// </summary>
[TestFixture]
public class PaneFileTests
{
    private const string Inspector =
        "<Pane xmlns=\"http://adamantium/ui\" Header=\"Inspector\" Kind=\"Tool\" Zone=\"Right\" MinSize=\"120\">" +
        "<TextBlock Text=\"Nothing selected\"/>" +
        "</Pane>";

    [Test]
    public void APaneFile_IsAClassOfItsOwn()
    {
        var project = AumlCodegenHarness.Project(new Dictionary<string, string> { ["Panes/InspectorPane.auml"] = Inspector });
        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors.Select(e => e.GetMessage())));

        var type = project.Load().GetType("Test.App.Panes.InspectorPane");
        Assert.That(type, Is.Not.Null);

        var pane = (Pane)Activator.CreateInstance(type);

        Assert.Multiple(() =>
        {
            Assert.That(pane.Header, Is.EqualTo("Inspector"));
            Assert.That(pane.Kind, Is.EqualTo(PaneKind.Tool));
            Assert.That(pane.Zone, Is.EqualTo(DockZone.Right));
            Assert.That(pane.MinSize, Is.EqualTo(120));
            Assert.That(pane.Content, Is.InstanceOf<TextBlock>());
        });
    }

    [Test]
    public void APaneFile_IsPlacedInADockingAreaByName()
    {
        var project = AumlCodegenHarness.Project(new Dictionary<string, string>
        {
            ["Panes/InspectorPane.auml"] = Inspector,
            ["Views/EditorView.auml"] =
                "<View xmlns=\"http://adamantium/ui\" xmlns:panes=\"clr-namespace:Test.App.Panes\">" +
                "<DockingArea><Pane Header=\"Scene\"/><panes:InspectorPane/></DockingArea>" +
                "</View>"
        });

        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors.Select(e => e.GetMessage())));
    }

    [Test]
    public void APaneFile_NamesItsViewModel()
    {
        _ = typeof(NavigationParameters);   // loaded, so the generator's compilation sees its assembly

        var project = AumlCodegenHarness.Project(new Dictionary<string, string>
        {
            ["Panes/InspectorPane.auml"] =
                "<Pane xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\" " +
                "xmlns:nav=\"clr-namespace:Adamantium.Navigation;assembly=Adamantium.Navigation\" " +
                "x:ViewModel=\"{x:Type nav:NavigationParameters}\" Header=\"Inspector\"/>"
        });
        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors.Select(e => e.GetMessage())));

        var pane = (Pane)Activator.CreateInstance(project.Load().GetType("Test.App.Panes.InspectorPane"));

        Assert.That(pane.ViewModelType, Is.EqualTo(typeof(NavigationParameters)));
    }
}
