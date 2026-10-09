using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>An element name is offered only for what markup can write as an element: a type the build can create, or the
/// owner of attached properties - a static Get/Set pair - a property element names. Automation peers, interfaces, enums,
/// abstract and static classes, attributes, event data and exceptions are not elements; nor is an abstract markup
/// extension an extension.</summary>
[TestFixture]
public class AumlElementCompletionTests
{
    private const string Probe = """
        namespace Probe;

        public class ProbePanel : Adamantium.UI.Controls.Panels.StackPanel { }

        public abstract class ProbeShape : Adamantium.UI.Controls.Panels.StackPanel { }

        public class ProbeNeedsOwner
        {
            public ProbeNeedsOwner(ProbePanel owner) { }
        }

        public static class ProbeHelpers
        {
            public static int Twice(int value) => value * 2;
        }
        """;

    private const string Root =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions" xmlns:local="clr-namespace:Probe">""";

    private CompletionEngine _engine;
    private string _probeFile;

    [OneTimeSetUp]
    public void BuildModel()
    {
        _probeFile = Path.Combine(Path.GetTempPath(), $"aumlprobe-{Guid.NewGuid():N}.cs");
        File.WriteAllText(_probeFile, Probe);

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            byName[Path.GetFileName(dll)] = dll;
        }

        foreach (var dll in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            byName.TryAdd(Path.GetFileName(dll), dll);
        }

        _engine = new CompletionEngine(AumlTypeModel.Build(byName.Values, [_probeFile]));
    }

    [OneTimeTearDown]
    public void RemoveProbe() => File.Delete(_probeFile);

    [Test]
    public void TheFrameworksElements_AreOffered_AndWhatCannotBeOneIsNot()
    {
        var labels = Labels("<|");

        Assert.That(labels, Is.SupersetOf(new[] { "Button", "Border", "ResourceContext", "AutomationProperties" }));
        Assert.That(labels, Has.None.AnyOf("ButtonAutomationPeer", "AutomationPeer", "IHitTestChildren", "AutomationControlType",
            "HorizontalAlignment", "LayerProbe", "ContentAttribute", "RoutedEventArgs", "AdamantiumPropertyException",
            "AdamantiumComponent", "IAdamantiumComponent", "Clipboard", "IClipboard"));
    }

    [Test]
    public void TheMarkupExtensions_AreOffered_ButNotAnAbstractOne()
    {
        var labels = Labels("""<Border Tag="{|"/>""");

        Assert.That(labels, Is.SupersetOf(new[] { "Binding", "MultiBinding", "Localize", "ResourceReference", "ThemeResource" }));
        Assert.That(labels, Has.None.AnyOf("BindingBase", "MarkupExtension"));
    }

    [Test]
    public void TheProjectsElements_AreOffered_AndWhatCannotBeOneIsNot() =>
        Assert.That(Labels("<local:|"), Is.EquivalentTo(new[] { "ProbePanel" }));

    private IReadOnlyList<string> Labels(string body)
    {
        var marked = Root + body;
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);
        return _engine.Complete(text, caret).Select(i => i.Label).ToList();
    }
}
