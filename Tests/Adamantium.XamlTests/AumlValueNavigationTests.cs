using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>What a value names, a Ctrl+click away: the member of an <c>{x:Static}</c>, the phrase of a <c>{Localize}</c> in
/// the table's base language file.</summary>
[TestFixture]
public class AumlValueNavigationTests
{
    private const string English = """
        <Language>
            <Phrase Key="Scene" Text="Scene"/>
            <Phrase Key="ViewedCamera" Text="Camera"/>
        </Language>
        """;

    private const string Probe = """
        namespace Probe;

        public sealed class Strings : Adamantium.UI.Core.Localization.LocalizedStrings
        {
            public static Strings Current { get; } = new();

            /// <summary>Scene</summary>
            public static string Scene => Current.Localized(nameof(Scene));

            /// <summary>Camera</summary>
            public static string ViewedCamera => Current.Localized(nameof(ViewedCamera));
        }

        public sealed class CameraChoices
        {
            public static string[] Views { get; } = ["Free"];
        }
        """;

    private const string Root =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions" xmlns:local="clr-namespace:Probe">""";

    private string _project;
    private string _probeFile;
    private string _english;
    private string _view;
    private DefinitionEngine _engine;

    [OneTimeSetUp]
    public void CreateProject()
    {
        _project = Path.Combine(Path.GetTempPath(), $"aumlvalues-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_project);
        File.WriteAllText(Path.Combine(_project, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><RootNamespace>Probe</RootNamespace></PropertyGroup></Project>");
        _english = Path.Combine(_project, "Strings.en.alang");
        File.WriteAllText(_english, English);
        _view = Path.Combine(_project, "View.auml");
        _probeFile = Path.Combine(_project, "Probe.cs");
        File.WriteAllText(_probeFile, Probe);
        _engine = new DefinitionEngine(AumlTypeModel.Build(References(), [_probeFile]));
    }

    [OneTimeTearDown]
    public void RemoveProject() => Directory.Delete(_project, true);

    [TestCase("""<ListBox ItemsSource="{x:Static local:CameraChoices.Vi|ews}"/>""")]
    [TestCase("""<ListBox ItemsSource="{x:Static local:CameraChoices.|Views}"/>""")]
    public void AStaticMember_GoesToItsDeclaration(string body)
    {
        var at = Definition(body);

        Assert.Multiple(() =>
        {
            Assert.That(at?.FilePath, Is.EqualTo(_probeFile));
            Assert.That(at?.StartLine, Is.EqualTo(Line(Probe, "Views")));
        });
    }

    [Test]
    public void AStaticMembersType_StillGoesToTheType()
    {
        var at = Definition("""<ListBox ItemsSource="{x:Static local:CameraCho|ices.Views}"/>""");

        Assert.That(at?.StartLine, Is.EqualTo(Line(Probe, "class CameraChoices")));
    }

    [TestCase("""<TextBlock Text="{Localize Strings.Viewed|Camera}"/>""")]
    [TestCase("""<TextBlock Text="{Localize local:Strings.ViewedCamera|}"/>""")]
    public void APhrase_GoesToWhereTheBaseLanguageWritesIt(string body)
    {
        var at = Definition(body);

        Assert.Multiple(() =>
        {
            Assert.That(at?.FilePath, Is.EqualTo(_english));
            Assert.That(at?.StartLine, Is.EqualTo(Line(English, "ViewedCamera")));
        });
    }

    [Test]
    public void ATable_GoesToItsBaseLanguageFile()
    {
        var at = Definition("""<TextBlock Text="{Localize Str|ings.Scene}"/>""");

        Assert.Multiple(() =>
        {
            Assert.That(at?.FilePath, Is.EqualTo(_english));
            Assert.That(at?.StartLine, Is.EqualTo(0));
        });
    }

    private DefinitionLocation Definition(string body)
    {
        var marked = Root + body + "</Window>";
        var caret = marked.IndexOf('|');
        return _engine.Definition(marked.Remove(caret, 1), caret, _view);
    }

    private static int Line(string text, string part) => text[..text.IndexOf(part, StringComparison.Ordinal)].Split('\n').Length - 1;

    private static IEnumerable<string> References()
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

        return byName.Values;
    }
}
