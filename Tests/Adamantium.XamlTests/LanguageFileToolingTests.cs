using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The editor helps with language files the way the build judges them: it completes what a file may hold,
/// reports the build's own problems while the file is typed, fills a translation in, and completes <c>{Localize}</c>
/// in a view.</summary>
[TestFixture]
public class LanguageFileToolingTests
{
    private const string English = """
        <Language>
            <Phrase Key="Close">Close</Phrase>
            <Phrase Key="PageOf">Page {page} of {count}</Phrase>
        </Language>
        """;

    private const string Probe = """
        namespace Probe;

        public sealed class Strings : Adamantium.UI.Core.Localization.LocalizedStrings, Adamantium.UI.Core.Localization.ILanguageTable
        {
            public static Strings Current { get; } = new();

            /// <summary>Close</summary>
            public static string Close => Current.Localized(nameof(Close));

            /// <summary>Page {page} of {count}</summary>
            public static string PageOf(object page, object count) => Current.Localized(nameof(PageOf), page, count);

            System.Collections.Generic.IReadOnlyList<string> Adamantium.UI.Core.Localization.ILanguageTable.Languages { get; } = ["en"];

            System.Collections.Generic.IReadOnlyList<string> Adamantium.UI.Core.Localization.ILanguageTable.Keys { get; } = [nameof(Close), nameof(PageOf)];

            System.Collections.Generic.IReadOnlyList<string> Adamantium.UI.Core.Localization.ILanguageTable.PlaceholdersOf(string key) =>
                key == nameof(PageOf) ? ["page", "count"] : [];

            string Adamantium.UI.Core.Localization.ILanguageTable.ChooserOf(string key) => null;

            string Adamantium.UI.Core.Localization.ILanguageTable.Find(string key, string language, object choice) => key switch
            {
                nameof(Close) => "Close",
                nameof(PageOf) => "Page {0} of {1}",
                _ => null,
            };
        }
        """;

    private const string Root =
        """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions" xmlns:local="clr-namespace:Probe">""";

    private string _project;
    private string _probeFile;
    private AumlTypeModel _model;

    [OneTimeSetUp]
    public void CreateProject()
    {
        _project = Path.Combine(Path.GetTempPath(), $"alang-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_project);
        File.WriteAllText(Path.Combine(_project, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>WinExe</OutputType><RootNamespace>Probe.App</RootNamespace></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(_project, "Strings.en.alang"), English);

        _probeFile = Path.Combine(_project, "Strings.cs");
        File.WriteAllText(_probeFile, Probe);
        _model = AumlTypeModel.Build(References(), [_probeFile]);
    }

    [OneTimeTearDown]
    public void RemoveProject() => Directory.Delete(_project, true);

    [Test]
    public void ATranslation_IsOfferedTheKeysItLacks_WithTheirBaseText()
    {
        var items = Complete("""
            <Language>
                <Phrase Key="Close">Закрыть</Phrase>
                <Phrase Key="|"
            """);

        Assert.That(items.Select(i => (i.Label, i.Detail)), Is.EquivalentTo(new[] { ("PageOf", "Page {page} of {count}") }));
    }

    [TestCase("<Language>\n    <|", "Phrase", "Language.Format")]
    [TestCase("<Language>\n    <Language.Format ShortTime=\"HH:mm\" |/>", "ShortDate", "LongDate", "LongTime", "DecimalSeparator", "GroupSeparator", "FirstDayOfWeek")]
    [TestCase("<Language>\n    <Phrase |", "Key", "Text", "Count", "Select", "One", "Few", "Many", "Other")]
    [TestCase("<Language>\n    <Phrase Key=\"Close\" |/>", "Text", "Count", "Select", "One", "Few", "Many", "Other")]
    [TestCase("<Language>\n    <Phrase Key=\"Actions\" Select=\"on\" |/>", "True", "False", "Other")]
    [TestCase("<Language>\n    <Phrase Key=\"Files\" Count=\"count\" One=\"\" |/>", "Few", "Many", "Other")]
    [TestCase("<Language>\n    <Phrase Key=\"Close\" Text=\"\" |/>")]
    public void AFile_IsOfferedWhatItMayHold(string marked, params string[] expected) =>
        Assert.That(Complete(marked).Select(i => i.Label), Is.EquivalentTo(expected));

    [Test]
    public void AFormat_IsOfferedTheLanguagesOwnWayFirst()
    {
        var days = Complete("<Language>\n    <Language.Format FirstDayOfWeek=\"|\"/>").Select(i => i.Label);
        var time = Complete("<Language>\n    <Language.Format ShortTime=\"|\"/>").Select(i => i.Label).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(days, Does.Contain("Monday"));
            Assert.That(time[0], Is.EqualTo(CultureInfo.GetCultureInfo("ru").DateTimeFormat.ShortTimePattern), "how Russian writes the time");
            Assert.That(time, Does.Contain("h:mm tt"));
        });
    }

    [Test]
    public void TheBuildsProblems_AreShownWhileTyping()
    {
        const string russian = """
            <Language>
                <Phrase Key="Open">Открыть</Phrase>
                <Phrase Key="Close">Закрыть {now}</Phrase>
            </Language>
            """;

        var found = LanguageFileValidator.Validate(RussianPath, russian, _ => null, null);

        Assert.Multiple(() =>
        {
            Assert.That(found.Single(d => d.Code == "AUL005").Line, Is.EqualTo(1));
            Assert.That(found.Single(d => d.Code == "AUL006").Line, Is.EqualTo(2));
            Assert.That(found.Single(d => d.Code == "AUL007").IsWarning, Is.True);
        });
    }

    [Test]
    public void AnOpenBaseFile_IsReadAsTyped()
    {
        const string englishTyped = """
            <Language>
                <Phrase Key="Close">Close</Phrase>
                <Phrase Key="PageOf">Page {page} of {count}</Phrase>
                <Phrase Key="Open">Open</Phrase>
            </Language>
            """;

        var found = LanguageFileValidator.Validate(RussianPath, """<Language><Phrase Key="Open">Открыть</Phrase></Language>""",
            path => path.EndsWith("Strings.en.alang", StringComparison.Ordinal) ? englishTyped : null, null);

        Assert.That(found.Select(d => d.Code), Does.Not.Contain("AUL005"));
    }

    [Test]
    public void TheQuickFix_AddsTheMissingStrings_EmptyUnderTheirBaseText()
    {
        const string russian = "<Language>\n    <Phrase Key=\"Close\">Закрыть</Phrase>\n</Language>\n";
        var context = LanguageFileContext.Of(RussianPath, russian, _ => null, null);

        var action = LanguageFileAssist.Actions(context, russian).Single();
        var fixedText = Apply(russian, action.Edits.Single());

        Assert.Multiple(() =>
        {
            Assert.That(fixedText, Is.EqualTo(
                "<Language>\n    <Phrase Key=\"Close\">Закрыть</Phrase>\n    <!-- Page {page} of {count} -->\n    <Phrase Key=\"PageOf\" Text=\"\"/>\n</Language>\n"));
            Assert.That(LanguageFileValidator.Validate(RussianPath, fixedText, _ => null, null).Select(d => d.Code),
                Is.EquivalentTo(new[] { "AUL007" }), "an empty string is not translated yet");
        });
    }

    [Test]
    public void TheQuickFix_CountsAPhraseTheBaseCounts_InTheFormsOfItsLanguage()
    {
        const string english = """<Language><Phrase Key="Files" Count="count" One="{count} file" Other="{count} files"/></Language>""";
        const string russian = "<Language>\n</Language>\n";
        var context = LanguageFileContext.Of(RussianPath, russian,
            path => path.EndsWith("Strings.en.alang", StringComparison.Ordinal) ? english : null, null);

        var fixedText = Apply(russian, LanguageFileAssist.Actions(context, russian).Single().Edits.Single());

        Assert.That(fixedText, Does.Contain("<Phrase Key=\"Files\" Count=\"count\" One=\"\" Few=\"\" Many=\"\" Other=\"\"/>"));
    }

    [Test]
    public void AStringOfATranslation_ShowsItsBaseTextOnHover()
    {
        const string russian = "<Language>\n    <Phrase Key=\"PageOf\">{count}: страница {page}</Phrase>\n</Language>\n";
        var context = LanguageFileContext.Of(RussianPath, russian, _ => null, null);

        var hover = LanguageFileAssist.Hover(context, russian, russian.IndexOf("PageOf", StringComparison.Ordinal) + 2);

        Assert.That(hover, Does.Contain("Page {page} of {count}").And.Contain("Strings.en.alang"));
    }

    [Test]
    public void AGeneratedTable_IsFoundWithItsTextAndPlaceholders()
    {
        var project = LanguageProject.Load(Path.Combine(_project, "App.csproj"));
        var compilation = CSharpCompilation.Create("Tables", [], References().Select(r => MetadataReference.CreateFromFile(r)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tables = LanguageTables.Collect(LanguageTableRun.Run(compilation, project, LanguageTableRun.Texts(project, _ => null), out _));
        var strings = tables.Single(t => t.FullName == "Probe.App.Strings").Strings;

        Assert.Multiple(() =>
        {
            Assert.That(strings.Single(s => s.Key == "Close").Text, Is.EqualTo("Close"));
            Assert.That(strings.Single(s => s.Key == "PageOf").Parameters, Is.EqualTo(new[] { "page", "count" }));
        });
    }

    [TestCase("""<TextBlock Text="{Localize Strings.|}"/>""", "Close", "PageOf")]
    [TestCase("""<TextBlock Text="{Localize local:Strings.|}"/>""", "Close", "PageOf")]
    [TestCase("""<TextBlock Text="{Localize Strings.PageOf, |}"/>""", "page", "count")]
    [TestCase("""<TextBlock Text="{Localize Strings.PageOf, page={Binding Page}, |}"/>""", "count")]
    public void Localize_CompletesTablesStringsAndPlaceholders(string body, params string[] expected) =>
        Assert.That(Labels(body), Is.EquivalentTo(expected));

    [Test]
    public void Localize_OffersTheProjectsTablesAndTheFrameworks() =>
        Assert.That(Labels("""<TextBlock Text="{Localize |}"/>"""), Is.SupersetOf(new[] { "Strings", "CanvasStrings", "DataGridStrings" }));

    [Test]
    public void AfterTheBrace_LocalizeIsOffered() =>
        Assert.That(Labels("""<TextBlock Text="{Loc|}"/>"""), Does.Contain("Localize"));

    private IReadOnlyList<string> Labels(string body)
    {
        var marked = Root + body + "</Window>";
        var caret = marked.IndexOf('|');
        return new CompletionEngine(_model).Complete(marked.Remove(caret, 1), caret).Select(i => i.Label).ToList();
    }

    private string RussianPath => Path.Combine(_project, "Strings.ru.alang");

    private IReadOnlyList<AumlCompletionItem> Complete(string marked)
    {
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);
        return LanguageFileCompletion.Complete(LanguageFileContext.Of(RussianPath, text, _ => null, null), text, caret);
    }

    private static string Apply(string text, AumlTextEdit edit)
    {
        var lines = text.Split('\n');
        var offset = lines.Take(edit.StartLine).Sum(l => l.Length + 1) + edit.StartCharacter;
        return text.Insert(offset, edit.NewText);
    }

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
