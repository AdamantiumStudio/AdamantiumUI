using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Text;
using Adamantium.UI.Platforms.MacOS;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What macOS gives an application besides its windows: spelling, the Trash, starting at sign-in, the keys of its
/// keyboard.</summary>
[TestFixture]
[Platform("MacOsX")]
public class MacOSSystemTests
{
    [Test]
    public void Spelling_FindsTheMisspelledWords_AndWhatTheyMeant()
    {
        var spelling = new MacOSSpelling();
        Assume.That(spelling.Supports("en-US"));

        var found = spelling.Check("This is a misteke and anothr one", "en-US");

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.EqualTo(new[] { new TextRange(10, 7), new TextRange(22, 6) }));
            Assert.That(spelling.Suggest("misteke", "en-US"), Does.Contain("mistake"));
        });
    }

    [Test]
    public void Spelling_ReportsAMisspelledWordThatIsTheWholeText()
    {
        var spelling = new MacOSSpelling();
        Assume.That(spelling.Supports("en"));

        Assert.That(spelling.Check("misteke", "en"), Is.EqualTo(new[] { new TextRange(0, 7) }));
    }

    [Test]
    public void Spelling_TakesAWordIgnored()
    {
        var spelling = new MacOSSpelling();
        Assume.That(spelling.Supports("en"));

        spelling.IgnoreAll("misteke", "en");

        Assert.That(spelling.Check("a misteke", "en"), Is.Empty);
    }

    [Test]
    public void Spelling_OfALanguageTheSystemDoesNotKnow_FindsNothing()
    {
        var spelling = new MacOSSpelling();

        Assert.Multiple(() =>
        {
            Assert.That(spelling.Supports("tlh"), Is.False);
            Assert.That(spelling.Check("nuqneH", "tlh"), Is.Empty);
            Assert.That(spelling.Suggest("nuqneH", "tlh"), Is.Empty);
        });
    }

    [Test]
    public async Task Words_GoOnFromTheirBeginning()
    {
        var spelling = new MacOSSpelling();
        Assume.That(spelling.Supports("en"));

        var words = await spelling.CompleteAsync("en", "hel", [], 3, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(words, Has.Count.LessThanOrEqualTo(3).And.Not.Empty);
            Assert.That(words.All(word => word.StartsWith("hel", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void StartingAtSignIn_IsListedAndTakenOut()
    {
        var folder = Directory.CreateTempSubdirectory("adamantium-agents").FullName;
        try
        {
            var startup = new LaunchAgentStartup(folder, "test.agent", ["/Applications/A & B.app/run <x>", "--go"]);
            Assert.That(startup.IsOn, Is.False);

            startup.IsOn = true;
            Assert.That(startup.IsOn, Is.True);
            Assert.That(new LaunchAgentStartup(folder, "test.agent", ["/elsewhere"]).IsOn, Is.False,
                "another program under its name is not this one");

            startup.IsOn = false;
            Assert.That(startup.IsOn, Is.False);
            Assert.That(Directory.EnumerateFileSystemEntries(folder), Is.Empty);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void APropertyListThatIsBroken_IsNotOn()
    {
        var folder = Directory.CreateTempSubdirectory("adamantium-agents").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "test.agent.plist"), "<plist><dict>");

            Assert.That(new LaunchAgentStartup(folder, "test.agent", ["/a"]).IsOn, Is.False);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void AFileDialog_OffersTheFilesOfAllItsKinds_AndEveryFileForOneOfAny()
    {
        var pictures = new FileType("Pictures", "png", "jpg");
        var png = new FileType("PNG", "png");

        Assert.Multiple(() =>
        {
            Assert.That(MacOSFileDialog.Extensions([pictures, png]), Is.EqualTo(new[] { "png", "jpg" }));
            Assert.That(MacOSFileDialog.Extensions([pictures, new FileType("All files", "*")]), Is.Empty);
            Assert.That(MacOSFileDialog.Extensions([pictures, new FileType("Anything")]), Is.Empty);
            Assert.That(MacOSFileDialog.Extensions(null), Is.Empty);
        });
    }

    [Test]
    public void WhatIsNotThere_DoesNotGoToTheTrash()
    {
        Assert.That(new FinderTrash().MoveToTrash(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())), Is.False);
    }

    [Test]
    public void AKey_HasTheCodesOfItsPlaces()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MacOSKeys.CodesOf(Key.Enter, []), Is.EquivalentTo(new ushort[] { 0x24, 0x4C }), "the pad's too");
            Assert.That(MacOSKeys.CodesOf(Key.A, MacOSKeys.InLayout(code => code == 0x0C ? 'a' : '\0')),
                Is.EqualTo(new ushort[] { 0x0C }), "where the layout has it");
        });
    }
}
