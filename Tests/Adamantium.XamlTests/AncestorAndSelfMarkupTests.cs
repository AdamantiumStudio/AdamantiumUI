using System.Linq;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>{Ancestor} and {Self} are bindings, so markup takes them where it reads a binding - an x:Load condition, a
/// {Localize} key (an {ObservableResource} key: ObservableResourceTests) - and they take what any binding is told of its
/// value.</summary>
[TestFixture]
public class AncestorAndSelfMarkupTests
{
    private const string Namespaces = "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private static StackPanel Load(string body, string attributes = "")
    {
        var result = AumlLoader.Load($"<StackPanel {Namespaces} Width=\"42\" {attributes}>{body}</StackPanel>");
        Assert.That(result.Root, Is.Not.Null, string.Join(" | ", result.Diagnostics));
        return (StackPanel)result.Root;
    }

    [Test]
    public void AnAncestorsProperty_IsFormatted()
    {
        var panel = Load("<TextBlock Text=\"{Ancestor StackPanel, Width, StringFormat='Width {0}'}\"/>");

        Assert.That(((TextBlock)panel.Children[0]).Text, Is.EqualTo("Width 42"));
    }

    [Test]
    public void AnElementsOwnProperty_IsFormatted_ItsModeTaken()
    {
        var panel = Load("<TextBlock Height=\"3\" Text=\"{Self Height, StringFormat='Height {0}', Mode=OneWay}\"/>");

        Assert.That(((TextBlock)panel.Children[0]).Text, Is.EqualTo("Height 3"));
    }

    [Test]
    public void AnXLoadCondition_OnAnAncestor_BuildsTheElementWhileItHolds()
    {
        var panel = Load("<Border x:Name=\"Late\" x:Load=\"{Ancestor StackPanel, IsEnabled}\"/>", "IsEnabled=\"False\"");
        var before = HasBorder(panel);
        panel.IsEnabled = true;
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False, "not built while the ancestor is disabled");
            Assert.That(HasBorder(panel), Is.True, "built once it is enabled");
        });
    }

    [Test]
    public void APhrasesKey_ReadFromAnAncestor()
    {
        var project = AumlCodegenHarness.Project(new System.Collections.Generic.Dictionary<string, string>
        {
            ["Strings.en.alang"] = "<Language><Phrase Key=\"Close\">Close</Phrase></Language>",
            ["MainWindow.auml"] = AumlCodegenHarness.WindowHeader + "><StackPanel Tag=\"Close\">" +
                "<TextBlock Text=\"{Localize Strings, Key={Ancestor StackPanel, Tag}}\"/></StackPanel></Window>",
        });

        Assert.Multiple(() =>
        {
            Assert.That(project.Errors, Is.Empty, AumlCodegenHarness.Errors(project.Errors));
            Assert.That(project.Source, Does.Contain(".KeySource = anc"));
        });
    }

    // ...and a table, with its key, read off the element and its ancestor.
    [Test]
    public void APhrasesTableAndKey_ReadFromTheElementAndAnAncestor()
    {
        var project = AumlCodegenHarness.Project(new System.Collections.Generic.Dictionary<string, string>
        {
            ["MainWindow.auml"] = AumlCodegenHarness.WindowHeader + "><StackPanel Tag=\"Close\">" +
                "<TextBlock Text=\"{Localize Table={Self DataContext}, Key={Ancestor StackPanel, Tag}}\"/></StackPanel></Window>",
        });

        Assert.Multiple(() =>
        {
            Assert.That(project.Errors, Is.Empty, AumlCodegenHarness.Errors(project.Errors));
            Assert.That(project.Source, Does.Contain(".TableSource = self"));
            Assert.That(project.Source, Does.Contain(".KeySource = anc"));
        });
    }

    // A static member as a converter's parameter reaches it, on {Binding} as on {Ancestor}: it was dropped in silence.
    [Test]
    public void AStaticMember_IsAConvertersParameter()
    {
        var project = AumlCodegenHarness.Project(new System.Collections.Generic.Dictionary<string, string>
        {
            ["MainWindow.auml"] = AumlCodegenHarness.WindowHeader + "><StackPanel>" +
                "<TextBlock Text=\"{Binding Name, ConverterParameter={x:Static local:Limits.Most}}\"/>" +
                "<TextBlock Text=\"{Ancestor StackPanel, Tag, ConverterParameter={x:Static local:Limits.Most}}\"/>" +
                "</StackPanel></Window>",
        }, sources: ["namespace Test.App; public static class Limits { public const int Most = 7; }"]);

        Assert.Multiple(() =>
        {
            Assert.That(project.Errors, Is.Empty, AumlCodegenHarness.Errors(project.Errors));
            Assert.That(System.Text.RegularExpressions.Regex.Matches(project.Source,
                @"\.ConverterParameter = global::Test\.App\.Limits\.Most").Count, Is.EqualTo(2), project.Source);
        });
    }

    // A failure names the binding a resource's key is read with, as it is written.
    [Test]
    public void AResourcesKey_FromTheElementItself_IsNamedAsWritten()
    {
        var project = AumlCodegenHarness.Project(new System.Collections.Generic.Dictionary<string, string>
        {
            ["MainWindow.auml"] = AumlCodegenHarness.WindowHeader + " xmlns:local=\"clr-namespace:Test.App\">" +
                "<local:Follower Plain=\"{ObservableResource {Self Tag}}\"/></Window>",
        }, sources: ["namespace Test.App; public class Follower : Adamantium.UI.Controls.Decorators.Border { public object Plain { get; set; } }"]);

        Assert.That(project.Errors.Select(e => e.GetMessage()), Has.Some.Contains("{ObservableResource {Self}}"),
            AumlCodegenHarness.Errors(project.Errors));
    }

    private static bool HasBorder(StackPanel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is Border)
            {
                return true;
            }
        }

        return false;
    }
}
