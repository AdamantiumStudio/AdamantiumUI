using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Completion asked anywhere in a file being written - inside the XML declaration, in a file cut short, for a
/// file not on disk - answers with what fits or with nothing, and never throws.</summary>
[TestFixture]
public class AumlCompletionRobustnessTests
{
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

    [TestCase("<?xml version=\"|1.0\" encoding=\"utf-8\" ?>\n<Grid xmlns=\"http://adamantium/ui\"/>")]
    [TestCase("<?xml version=\"1.0\" encoding=\"|\" ?>\n<Grid xmlns=\"http://adamantium/ui\"/>")]
    [TestCase("<?xml version=\"1.0\" ?>\n<Grid xmlns=\"http://adamantium/ui\">\n    <Border Background=\"{ObservableResource |")]
    [TestCase("<Grid xmlns=\"http://adamantium/ui\">\n    <Image Source=\"Views/|")]
    [TestCase("<Grid xmlns=\"http://adamantium/ui\">\n    <TextBlock Text=\"|")]
    [TestCase("<|")]
    [TestCase("<Grid xmlns=\"http://adamantium/ui\" |")]
    public void CompletionAnywhere_NeverThrows(string marked)
    {
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);

        Assert.That(() => _engine.Complete(text, caret, "Views/NotOnDisk.auml"), Throws.Nothing);
    }
}
