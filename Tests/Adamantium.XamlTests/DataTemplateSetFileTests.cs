using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A data template set is a markup file of its own: the build makes it a class deriving from DataTemplateSet,
/// and other markup places it by name wherever a template selector goes.</summary>
[TestFixture]
public class DataTemplateSetFileTests
{
    private const string Namespaces = """xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions" """;

    private static readonly Dictionary<string, string> Files = new()
    {
        ["Templates/TabContents.auml"] = $"""
            <DataTemplateSet {Namespaces}>
                <DataTemplate x:DataType="{"{x:Type Brush}"}"><TextBlock Text="any brush"/></DataTemplate>
                <DataTemplate x:DataType="{"{x:Type SolidColorBrush}"}"><TextBlock Text="a solid brush"/></DataTemplate>
                <DataTemplate><TextBlock Text="anything else"/></DataTemplate>
            </DataTemplateSet>
            """,
        ["MainWindow.auml"] = $"""
            <Window {Namespaces} xmlns:templates="clr-namespace:Test.App.Templates">
                <ListBox>
                    <ListBox.ItemTemplateSelector>
                        <templates:TabContents/>
                    </ListBox.ItemTemplateSelector>
                </ListBox>
            </Window>
            """
    };

    [Test]
    public void ASetFile_IsAClass_PlacedByName()
    {
        var project = AumlCodegenHarness.Project(Files);
        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors.Select(e => e.GetMessage())));

        var set = (DataTemplateSet)Activator.CreateInstance(project.Load().GetType("Test.App.Templates.TabContents"));

        Assert.That(set.Templates, Has.Count.EqualTo(3));
        Assert.That(set.SelectTemplate(new SolidColorBrush(), null).DataType, Is.EqualTo(typeof(SolidColorBrush)));
        Assert.That(set.SelectTemplate("text", null).DataType, Is.Null);
        Assert.That(project.Source, Does.Contain("new global::Test.App.Templates.TabContents()"));
    }

    [Test]
    public void TheNewFileTemplate_Builds()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "AdamantiumUI.sln")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        var skeleton = File.ReadAllText(Path.Combine(root, "editors", "rider-auml", "src", "main", "resources", "fileTemplates",
            "internal", "Adamantium DataTemplateSet.auml.ft"));

        var project = AumlCodegenHarness.Project(new Dictionary<string, string> { ["ItemTemplates.auml"] = skeleton });

        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors.Select(e => e.GetMessage())));
        Assert.That(project.Load().GetType("Test.App.ItemTemplates")?.BaseType, Is.EqualTo(typeof(DataTemplateSet)));
    }
}
