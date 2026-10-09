using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Adamantium.UI.LanguageServer;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.Localization;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>What the markup's build and its language server say, in English and in Russian: every message in both, with
/// the same placeholders, and said in the language of the UI culture - the server's as its client names it.</summary>
[TestFixture]
public class MarkupMessagesTests
{
    private static readonly MessageCatalog[] Catalogs = [MarkupMessages.Catalog, ServerMessages.Catalog];

    [Test]
    public void EveryMessage_IsInBothLanguages_WithTheSamePlaceholders()
    {
        Assert.Multiple(() =>
        {
            foreach (var catalog in Catalogs)
            {
                var english = catalog.Phrases("en");
                var russian = catalog.Phrases("ru");
                Assert.That(english, Is.Not.Empty);
                Assert.That(russian.Keys, Is.EquivalentTo(english.Keys));
                foreach (var (key, text) in english)
                {
                    if (russian.TryGetValue(key, out var translated))
                    {
                        Assert.That(Placeholders(translated), Is.EquivalentTo(Placeholders(text)), key);
                    }
                }
            }
        });
    }

    [TestCase("en-US")]
    [TestCase("ru-RU")]
    public void EveryMethod_SaysAMessageOfItsCatalog(string language)
    {
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            Assert.Multiple(() =>
            {
                foreach (var method in Methods(typeof(MarkupMessages)).Concat(Methods(typeof(ServerMessages))))
                {
                    var said = (string)method.Invoke(null, method.GetParameters().Select(Sample).ToArray());
                    Assert.That(said, Does.Not.Match("^[A-Z][A-Za-z]+$"), method.Name);
                }

                foreach (var directive in AumlDirectives.All)
                {
                    Assert.That(directive.Description, Does.Not.StartWith("Directive"), directive.Name);
                }
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    [Test]
    public void AMessage_IsSaidInTheUICulturesLanguage_AndInEnglishForOneWithoutAFile()
    {
        var culture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            var russian = MarkupMessages.TypeNotInNamespace("Grid", "Probe");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var german = MarkupMessages.TypeNotInNamespace("Grid", "Probe");

            Assert.That(russian, Is.EqualTo("Тип Grid не найден в пространстве имён Probe"));
            Assert.That(german, Is.EqualTo("Type Grid could not be found in namespace Probe"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    [Test]
    public void TheServer_SpeaksTheLanguageItsClientNames()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        var uri = new Uri(Path.Combine(folder, "Lonely.auml")).AbsoluteUri;
        var culture = CultureInfo.CurrentUICulture;
        List<JsonNode> said;
        try
        {
            said = Session(
                Request(1, "initialize", new JsonObject { ["capabilities"] = new JsonObject(), ["locale"] = "ru" }),
                Notification("textDocument/didOpen", new JsonObject
                {
                    ["textDocument"] = new JsonObject
                    {
                        ["uri"] = uri, ["languageId"] = "auml", ["version"] = 1, ["text"] = "<Grid/>"
                    }
                }),
                Request(2, "shutdown", null),
                Notification("exit", null));
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = null;
            CultureInfo.CurrentUICulture = culture;
            Directory.Delete(folder, true);
        }

        var messages = said.Where(m => m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics")
            .SelectMany(m => m["params"]["diagnostics"].AsArray())
            .Select(d => d["message"].GetValue<string>())
            .ToList();
        Assert.That(messages, Has.Some.StartsWith("Файл не входит ни в один проект"));
    }

    private static IEnumerable<MethodInfo> Methods(Type catalog) =>
        catalog.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(string) && !(catalog == typeof(MarkupMessages) && m.Name == nameof(MarkupMessages.Directive)));

    private static object Sample(ParameterInfo parameter) => parameter.ParameterType switch
    {
        var type when type == typeof(int) => 2,
        var type when type == typeof(bool) => true,
        var type when type == typeof(IReadOnlyCollection<string>) || type == typeof(IReadOnlyList<string>) => new List<string> { "a", "b" },
        _ => "x"
    };

    private static IEnumerable<string> Placeholders(string text) =>
        Regex.Matches(text.Replace("{{", string.Empty).Replace("}}", string.Empty), @"\{(\w+)\}").Select(m => m.Groups[1].Value).Distinct();

    private static JsonObject Request(int id, string method, JsonNode @params) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = @params };

    private static JsonObject Notification(string method, JsonNode @params) =>
        new() { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params };

    private static List<JsonNode> Session(params JsonObject[] messages)
    {
        var input = new MemoryStream();
        foreach (var message in messages)
        {
            input.Write(LspServer.Frame(message.ToJsonString()));
        }

        input.Position = 0;
        var output = new MemoryStream();
        using (var workspace = new AumlWorkspace())
        {
            new LspServer(workspace, input, output).Run();
        }

        output.Position = 0;
        return LspServer.ReadFrames(output).ToList();
    }
}
