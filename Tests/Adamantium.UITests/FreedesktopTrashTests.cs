using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Adamantium.UI.Platforms.Linux;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>On Linux a deleted file goes to the freedesktop.org trash: into its <c>files</c>, with a <c>.trashinfo</c>
/// beside it in <c>info</c> saying where it was and when it went, so a file manager can put it back.</summary>
[TestFixture]
[Platform("Linux")]
[SupportedOSPlatform("linux")]
[NonParallelizable]
public class FreedesktopTrashTests
{
    private string _root;
    private string _dataHome;
    private string _previousDataHome;

    [SetUp]
    public void MakeFolders()
    {
        _root = Path.Combine(Path.GetTempPath(), "adamantium-trash-" + Guid.NewGuid().ToString("N"));
        _dataHome = Path.Combine(_root, "data");
        Directory.CreateDirectory(_dataHome);
        _previousDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _dataHome);
    }

    [TearDown]
    public void RemoveFolders()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _previousDataHome);
        Directory.Delete(_root, true);
    }

    [Test]
    public void AFile_GoesToTheTrash_SayingWhereItWas()
    {
        var file = Write("Level 1.ascene");

        var moved = new FreedesktopTrash().MoveToTrash(file);

        var info = File.ReadAllLines(Path.Combine(_dataHome, "Trash", "info", "Level 1.ascene.trashinfo"));
        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(File.Exists(file), Is.False);
            Assert.That(File.Exists(Path.Combine(_dataHome, "Trash", "files", "Level 1.ascene")), Is.True);
            Assert.That(info[0], Is.EqualTo("[Trash Info]"));
            Assert.That(info[1], Is.EqualTo("Path=" + file.Replace(" ", "%20")));
            Assert.That(DateTime.TryParse(info[2]["DeletionDate=".Length..], out _), Is.True, info[2]);
        });
    }

    [Test]
    public void ASecondFileOfTheSameName_GetsANameOfItsOwn()
    {
        var first = Write("a/player.cs");
        var second = Write("b/player.cs");

        var trash = new FreedesktopTrash();
        trash.MoveToTrash(first);
        trash.MoveToTrash(second);

        var files = Directory.GetFiles(Path.Combine(_dataHome, "Trash", "files")).Select(Path.GetFileName).Order().ToList();
        var infos = Directory.GetFiles(Path.Combine(_dataHome, "Trash", "info")).Select(Path.GetFileName).Order().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(files, Is.EqualTo(new[] { "player.2.cs", "player.cs" }));
            Assert.That(infos, Is.EqualTo(new[] { "player.2.cs.trashinfo", "player.cs.trashinfo" }));
        });
    }

    [Test]
    public void AFolder_GoesWhole()
    {
        Write("Models/ship.glb");
        var folder = Path.Combine(_root, "Models");

        var moved = new FreedesktopTrash().MoveToTrash(folder);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(Directory.Exists(folder), Is.False);
            Assert.That(File.Exists(Path.Combine(_dataHome, "Trash", "files", "Models", "ship.glb")), Is.True);
        });
    }

    [Test]
    public void AFileReachedThroughAFolderLink_IsTrashedFromWhereItReallyIs()
    {
        var file = Write("real/hull.png");
        Directory.CreateSymbolicLink(Path.Combine(_root, "linked"), Path.Combine(_root, "real"));

        var moved = new FreedesktopTrash().MoveToTrash(Path.Combine(_root, "linked", "hull.png"));

        var info = File.ReadAllLines(Path.Combine(_dataHome, "Trash", "info", "hull.png.trashinfo"));
        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(File.Exists(file), Is.False);
            Assert.That(info[1], Is.EqualTo("Path=" + file));
        });
    }

    [Test]
    public void ATrashThatCannotBeMade_LeavesTheFile_AndSaysSo()
    {
        var file = Write("Level 1.ascene");
        var locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(locked, "data"));

        var moved = new FreedesktopTrash().MoveToTrash(file);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.False);
            Assert.That(File.Exists(file), Is.True);
        });
    }

    [Test]
    public void AFileReachedThroughALinkedDataFolder_GoesToTheHomeTrash()
    {
        var file = Write("Level 1.ascene");
        var linkedData = Path.Combine(_root, "linked-data");
        Directory.CreateDirectory(_dataHome);
        Directory.CreateSymbolicLink(linkedData, _dataHome);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", linkedData);

        var moved = new FreedesktopTrash().MoveToTrash(file);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(File.Exists(Path.Combine(_dataHome, "Trash", "files", "Level 1.ascene")), Is.True);
        });
    }

    private string Write(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "x");
        return path;
    }
}
