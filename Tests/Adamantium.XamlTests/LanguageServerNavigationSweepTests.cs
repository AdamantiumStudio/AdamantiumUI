using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The language server, spoken to as an editor speaks to it, over the sandbox's own markup: from an icon's key
/// to the dictionary that declares it, and a rename of that key that reaches the dictionary and every use. Over the
/// built sandbox, as the server itself sees it.</summary>
[TestFixture]
[Explicit("Builds the sandbox's type model: seconds, and it needs the sandbox built.")]
[Category("MarkupSweep")]
public class LanguageServerNavigationSweepTests
{
    [Test]
    public void AnIconKey_LeadsToItsDictionary_AndRenamesEverywhere()
    {
        var root = RepositoryRoot();
        var view = Path.Combine(root, "Adamantium.UI.Sandbox", "Views", "RibbonShellView.auml");
        var text = File.ReadAllText(view);
        var at = text.IndexOf("{ObservableResource SaveDocumentIcon}", StringComparison.Ordinal) + "{ObservableResource ".Length + 2;
        var (line, character) = LineAndCharacter(text, at);
        var uri = new Uri(view).AbsoluteUri;
        var position = new JsonObject { ["line"] = line, ["character"] = character };

        var replies = Session(
            Request(1, "initialize", new JsonObject { ["capabilities"] = new JsonObject() }),
            Notification("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "auml", ["version"] = 1, ["text"] = text }
            }),
            Request(2, "textDocument/definition", Params(uri, position)),
            Request(3, "textDocument/prepareRename", Params(uri, position)),
            Request(4, "textDocument/rename", Params(uri, position, new JsonObject { ["newName"] = "SaveSceneIcon" })),
            Request(5, "textDocument/references", Params(uri, position)),
            Request(6, "shutdown", null),
            Notification("exit", null));

        Assert.That(replies[2]?["uri"]?.GetValue<string>(), Does.EndWith("Resources/RibbonShellIcons.auml"));
        Assert.That(replies[3]?["placeholder"]?.GetValue<string>(), Is.EqualTo("SaveDocumentIcon"));
        var changed = replies[4]?["changes"]?.AsObject().Select(c => c.Key).ToList();
        Assert.That(changed, Has.Some.EndsWith("Resources/RibbonShellIcons.auml").And.Some.EndsWith("Views/RibbonShellView.auml"));
        Assert.That(replies[5]?.AsArray().Count, Is.GreaterThanOrEqualTo(2));
    }

    private static JsonObject Params(string uri, JsonObject position, JsonObject extra = null)
    {
        var result = new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = position.DeepClone()
        };
        foreach (var (name, value) in extra ?? [])
        {
            result[name] = value?.DeepClone();
        }

        return result;
    }

    private static JsonObject Request(int id, string method, JsonNode @params) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = @params };

    private static JsonObject Notification(string method, JsonNode @params) =>
        new() { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params };

    private static Dictionary<int, JsonNode> Session(params JsonObject[] messages)
    {
        var input = new MemoryStream();
        foreach (var message in messages)
        {
            var body = Encoding.UTF8.GetBytes(message.ToJsonString());
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            input.Write(header);
            input.Write(body);
        }

        input.Position = 0;
        var output = new MemoryStream();
        using (var workspace = new AumlWorkspace())
        {
            new LspServer(workspace, input, output).Run();
        }

        var replies = new Dictionary<int, JsonNode>();
        var all = Encoding.UTF8.GetString(output.ToArray());
        foreach (var frame in all.Split("Content-Length:", StringSplitOptions.RemoveEmptyEntries))
        {
            var json = JsonNode.Parse(frame[(frame.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..]);
            if (json?["id"] is { } id && json["method"] == null)
            {
                replies[id.GetValue<int>()] = json["result"];
            }
        }

        return replies;
    }

    private static (int Line, int Character) LineAndCharacter(string text, int offset)
    {
        var before = text[..offset];
        var line = before.Count(c => c == '\n');
        return (line, offset - (before.LastIndexOf('\n') + 1));
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AdamantiumUI.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (AdamantiumUI.sln) is not above the test output.");
    }
}
