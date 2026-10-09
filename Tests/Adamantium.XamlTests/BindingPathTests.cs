using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A {Binding} path is checked when the build knows what it reads: the view's x:ViewModel, a template's
/// x:DataType, or a DataContext bound to a path of one of them - with the members the MVVM generator makes. Anywhere
/// else it is the runtime's to report.</summary>
[TestFixture]
public class BindingPathTests
{
    private const string Sources = """
        using System;

        namespace Adamantium.MVVM
        {
            [AttributeUsage(AttributeTargets.Field)] public sealed class BindableAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.Method)] public sealed class CommandAttribute : Attribute { public string Name { get; set; } }
        }

        namespace Probe
        {
            using Adamantium.MVVM;

            public sealed class Settings
            {
                public double Volume { get; set; }
            }

            public sealed class Item
            {
                public string Label { get; set; }
            }

            public partial class MainViewModel
            {
                [Bindable] private string _title;

                public Settings Settings { get; } = new();

                [Command] private void Save() { }

                [Command(Name = "Go")] private void Navigate() { }
            }
        }
        """;

    private const string Header =
        "<View xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\" " +
        "xmlns:vm=\"clr-namespace:Probe\" x:ViewModel=\"vm:MainViewModel\">";

    [TestCase("<TextBlock ⟦Text=\"{Binding Nope}\"/>", "MainViewModel has no Nope")]
    [TestCase("<TextBlock ⟦Text=\"{Binding Settings.Nope}\"/>", "Settings has no Nope")]
    [TestCase("<TextBlock ⟦Text=\"{Binding Path=Nope, Mode=OneWay}\"/>", "MainViewModel has no Nope")]
    [TestCase("<Grid DataContext=\"{Binding Settings}\"><TextBlock ⟦Text=\"{Binding Nope}\"/></Grid>", "Settings has no Nope")]
    [TestCase("<ItemsControl><ItemsControl.ItemTemplate><DataTemplate x:DataType=\"vm:Item\"><TextBlock ⟦Text=\"{Binding Nope}\"/></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>", "Item has no Nope")]
    public void APathItsSourceDoesNotHave_FailsTheBuild_AtTheBinding(string body, string expected)
    {
        var at = body.IndexOf('⟦');
        var markup = Header + body.Remove(at, 1) + "</View>";
        var column = Header.Length + at;

        var errors = Diagnose(markup);

        Assert.That(errors.Any(e => e.GetMessage().Contains(expected, StringComparison.Ordinal) &&
                                    e.Location.GetLineSpan().StartLinePosition.Character == column), Is.True,
            string.Join("\n", errors.Select(e => $"{e.Location.GetLineSpan().StartLinePosition} {e.GetMessage()}")));
    }

    [TestCase("<TextBlock Text=\"{Binding Title}\"/>")]
    [TestCase("<Button Command=\"{Binding SaveCommand}\"/>")]
    [TestCase("<Button Command=\"{Binding Go}\"/>")]
    [TestCase("<TextBlock Text=\"{Binding Settings.Volume}\"/>")]
    [TestCase("<TextBlock Text=\"{Binding}\"/>")]
    [TestCase("<TextBlock x:Name=\"Other\"/><TextBlock Text=\"{Binding Nope, ElementName=Other}\"/>")]
    [TestCase("<Grid DataContext=\"{Binding Settings}\"><TextBlock Text=\"{Binding Volume}\"/></Grid>")]
    [TestCase("<ItemsControl><ItemsControl.ItemTemplate><DataTemplate><TextBlock Text=\"{Binding Anything}\"/></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>")]
    [TestCase("<ItemsControl><ItemsControl.ItemTemplate><DataTemplate x:DataType=\"vm:Item\"><TextBlock Text=\"{Binding Label}\"/></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>")]
    public void APathItsSourceHas_OrOneTheBuildCannotKnow_Passes(string body)
    {
        var errors = Diagnose(Header + body + "</View>");

        Assert.That(errors.Where(e => e.GetMessage().Contains("binding path", StringComparison.Ordinal)), Is.Empty,
            string.Join("\n", errors.Select(e => e.GetMessage())));
    }

    [Test]
    public void WithoutAViewModel_NoPathIsJudged()
    {
        var errors = Diagnose("<View xmlns=\"http://adamantium/ui\"><TextBlock Text=\"{Binding Nope}\"/></View>");

        Assert.That(errors.Where(e => e.GetMessage().Contains("binding path", StringComparison.Ordinal)), Is.Empty);
    }

    private static IReadOnlyList<Diagnostic> Diagnose(string markup) =>
        AumlCodegenHarness.Project(new Dictionary<string, string> { ["Views/MainView.auml"] = markup }, sources: [Sources])
            .GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
}
