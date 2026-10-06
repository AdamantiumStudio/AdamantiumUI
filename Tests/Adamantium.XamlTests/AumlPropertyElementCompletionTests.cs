using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A property element offers what its owner has to set: its own properties, and the attached ones - for an owner
/// of attached properties only, like <c>ResourceContext</c>, those are all there is.</summary>
[TestFixture]
public class AumlPropertyElementCompletionTests
{
    private const string Root =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions">""";

    private CompletionEngine _engine;

    [OneTimeSetUp]
    public void BuildModel()
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            byName[Path.GetFileName(dll)] = dll;
        }

        foreach (var dll in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            byName.TryAdd(Path.GetFileName(dll), dll);
        }

        _engine = new CompletionEngine(AumlTypeModel.Build(byName.Values));
    }

    [TestCase("<Border><ResourceContext.|", "ResourceContext.Resources", "ResourceContext.Source")]
    [TestCase("<Border><ResourceContext.Res|", "ResourceContext.Resources")]
    [TestCase("<Grid><Grid.|", "Grid.ColumnDefinitions", "Grid.RowDefinitions")]
    public void APropertyElement_OffersWhatItsOwnerSets(string body, params string[] expected)
    {
        var marked = Root + body;
        var caret = marked.IndexOf('|');

        var labels = _engine.Complete(marked.Remove(caret, 1), caret).Select(i => i.Label).ToList();

        Assert.That(labels, Is.SupersetOf(expected));
    }
}
