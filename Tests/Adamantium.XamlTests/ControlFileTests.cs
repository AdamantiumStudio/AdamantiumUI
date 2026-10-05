using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Controls;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A file whose root is any control - a ribbon tab, one of its groups - is a class of its own, which other markup
/// places by name, so a large ribbon can be split into a file per tab and per group.</summary>
[TestFixture]
public class ControlFileTests
{
    private const string ClipboardGroup =
        "<RibbonGroup xmlns=\"http://adamantium/ui\" Header=\"Clipboard\">" +
        "<RibbonButton Content=\"Paste\"/>" +
        "<RibbonButton Content=\"Cut\"/>" +
        "</RibbonGroup>";

    private const string HomeTab =
        "<RibbonTab xmlns=\"http://adamantium/ui\" xmlns:groups=\"clr-namespace:Test.App.Ribbon.Groups\" Header=\"Home\">" +
        "<groups:ClipboardGroup/>" +
        "<RibbonGroup Header=\"Font\"/>" +
        "</RibbonTab>";

    private static GeneratedProject Ribbon() => AumlCodegenHarness.Project(new Dictionary<string, string>
    {
        ["Ribbon/Groups/ClipboardGroup.auml"] = ClipboardGroup,
        ["Ribbon/HomeTab.auml"] = HomeTab,
        ["Views/ShellView.auml"] =
            "<View xmlns=\"http://adamantium/ui\" xmlns:ribbon=\"clr-namespace:Test.App.Ribbon\">" +
            "<Ribbon><ribbon:HomeTab/></Ribbon>" +
            "</View>"
    });

    [Test]
    public void AControlFile_IsAClassOfItsOwn()
    {
        var project = AumlCodegenHarness.Project(new Dictionary<string, string> { ["Ribbon/Groups/ClipboardGroup.auml"] = ClipboardGroup });
        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors.Select(e => e.GetMessage())));

        var group = (RibbonGroup)Activator.CreateInstance(project.Load().GetType("Test.App.Ribbon.Groups.ClipboardGroup"));

        Assert.Multiple(() =>
        {
            Assert.That(group.Header, Is.EqualTo("Clipboard"));
            Assert.That(group.Items, Has.Count.EqualTo(2));
            Assert.That(group.Items[0], Is.InstanceOf<RibbonButton>());
        });
    }

    [Test]
    public void ATabFile_PlacesItsGroupFilesByName()
    {
        var project = Ribbon();
        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors.Select(e => e.GetMessage())));

        var assembly = project.Load();
        var tab = (RibbonTab)Activator.CreateInstance(assembly.GetType("Test.App.Ribbon.HomeTab"));

        Assert.Multiple(() =>
        {
            Assert.That(tab.Header, Is.EqualTo("Home"));
            Assert.That(tab.Items, Has.Count.EqualTo(2));
            Assert.That(tab.Items[0].GetType(), Is.EqualTo(assembly.GetType("Test.App.Ribbon.Groups.ClipboardGroup")));
            Assert.That(((RibbonGroup)tab.Items[0]).Items, Has.Count.EqualTo(2), "the group file's own content");
        });
    }
}
