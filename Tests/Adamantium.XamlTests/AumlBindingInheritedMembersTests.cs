using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A binding against an <c>x:ViewModel</c> is offered what the view model inherits too - the base class's
/// properties, and the ones the MVVM generator makes from its <c>[Bindable]</c> fields and <c>[Command]</c> methods.</summary>
[TestFixture]
public class AumlBindingInheritedMembersTests
{
    private const string Probe = """
        namespace Probe;

        public partial class PageViewModel
        {
            [Adamantium.MVVM.Bindable] private string _caption;

            public int PageCount { get; set; }

            [Adamantium.MVVM.Command] private void Close() { }
        }

        public partial class TerrainViewModel : PageViewModel
        {
            [Adamantium.MVVM.Bindable] private double _height;

            public string Biome { get; set; }
        }
        """;

    private const string Root =
        """<View xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions" xmlns:local="clr-namespace:Probe" x:ViewModel="local:TerrainViewModel">""";

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

    [TestCase("""<TextBlock Text="{Binding |}"/>""")]
    [TestCase("""<TextBlock Text="{Binding Path=|}"/>""")]
    public void ABinding_IsOfferedWhatTheViewModelInherits(string body)
    {
        var marked = Root + body + "</View>";
        var caret = marked.IndexOf('|');

        var labels = _engine.Complete(marked.Remove(caret, 1), caret).Select(i => i.Label).ToList();

        Assert.That(labels, Is.SupersetOf(new[] { "Height", "Biome", "Caption", "PageCount", "CloseCommand" }));
    }
}
