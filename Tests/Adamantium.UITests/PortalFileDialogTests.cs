using System.Collections.Generic;
using Adamantium.DBus;
using Adamantium.UI.Core;
using Adamantium.UI.Platforms.Linux;
using NUnit.Framework;

namespace Adamantium.UITests;

public class PortalFileDialogTests
{
    [Test]
    public void AnAnswer_GivesTheChosenPath()
    {
        Assert.That(PortalFileDialog.Chosen(Response(0, "file:///home/me/My%20graph.json")), Is.EqualTo("/home/me/My graph.json"));
    }

    [Test]
    public void AnAnswerThatIsNotALocalFile_GivesNothing()
    {
        Assert.That(PortalFileDialog.Chosen(Response(0, "sftp://host/home/me/dir")), Is.Null);
    }

    [Test]
    public void ACanceledDialog_GivesNothing()
    {
        Assert.That(PortalFileDialog.Chosen(Response(1, null)), Is.Null);
    }

    [Test]
    public void FileTypes_BecomeGlobFilters_TheFirstSelected()
    {
        var options = PortalFileDialog.Options([new FileType("Graphs", "json"), new FileType("Anything")]);

        var filters = (List<object>)options["filters"].Value;
        var first = (object[])filters[0];
        Assert.Multiple(() =>
        {
            Assert.That(filters, Has.Count.EqualTo(1), "a type with no extension filters nothing");
            Assert.That(first[0], Is.EqualTo("Graphs"));
            Assert.That(((List<object>)first[1]).ConvertAll(p => ((object[])p)[1]), Is.EqualTo(new[] { "*.json", "*.JSON" }));
            Assert.That(options["current_filter"].Value, Is.SameAs(first));
        });
    }

    [Test]
    public void AFolderIsAskedForAsADirectory()
    {
        var options = PortalFileDialog.FolderOptions();

        Assert.That(options["directory"].Signature, Is.EqualTo("b"));
        Assert.That(options["directory"].Value, Is.EqualTo(true));
    }

    [Test]
    public void ASaveDialog_OpensInTheFolderAsked_WhenItIsThere()
    {
        var folder = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);

        var options = PortalFileDialog.SaveOptions(new SaveFileRequest { FileName = "Arena (game).ascene", Folder = folder });
        var missing = PortalFileDialog.SaveOptions(new SaveFileRequest { Folder = System.IO.Path.Combine(folder, "No such folder") });

        Assert.Multiple(() =>
        {
            Assert.That(options["current_name"].Value, Is.EqualTo("Arena (game).ascene"));
            Assert.That(options["current_folder"].Signature, Is.EqualTo("ay"));
            Assert.That(options["current_folder"].Value, Is.EqualTo(System.Text.Encoding.UTF8.GetBytes(folder + "\0")));
            Assert.That(missing.ContainsKey("current_folder"), Is.False);
        });
    }

    private static DBusMessage Response(uint code, string uri) =>
        DBusMessage.MethodCall("x.y", "/x", "x.y", "Response", "ua{sv}", writer =>
        {
            writer.WriteUInt32(code);
            writer.WriteDictionary(uri == null
                ? new Dictionary<string, DBusVariant>()
                : new Dictionary<string, DBusVariant> { ["uris"] = new("as", new List<object> { uri }) });
        });
}
