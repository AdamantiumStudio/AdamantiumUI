using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Automation;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>An application started on a desktop of its own gets its environment as one block written here, not through
/// <see cref="System.Diagnostics.ProcessStartInfo"/>: what the launching process has, the automation's variables over it,
/// in the form the system reads.</summary>
[TestFixture]
public class HiddenDesktopEnvironmentTests
{
    private const string Inherited = "ADAM_HIDDEN_DESKTOP_TEST_INHERITED";

    [SetUp]
    public void SetVariable() => Environment.SetEnvironmentVariable(Inherited, "from the parent");

    [TearDown]
    public void ClearVariable() => Environment.SetEnvironmentVariable(Inherited, null);

    private static List<string> Entries(char[] block) =>
        new string(block).Split('\0').TakeWhile(entry => entry.Length > 0).ToList();

    [Test]
    public void TheBlockEndsInTwoZeros_AndEveryEntryIsANameAndAValue()
    {
        var block = HiddenDesktop.EnvironmentBlock(new Dictionary<string, string> { ["ADAM_AUTOMATION_PIPE"] = "p" });
        var entries = Entries(block);

        Assert.Multiple(() =>
        {
            Assert.That(block[^1], Is.EqualTo('\0'));
            Assert.That(block[^2], Is.EqualTo('\0'));
            Assert.That(new string(block).Length, Is.EqualTo(entries.Sum(entry => entry.Length + 1) + 1),
                "nothing after the closing zero");
            Assert.That(entries, Has.All.Contains('='));
        });
    }

    [Test]
    public void ThisProcesssVariablesGoAlong_AndTheAutomationsWinOverThem()
    {
        var entries = Entries(HiddenDesktop.EnvironmentBlock(new Dictionary<string, string>
        {
            ["ADAM_AUTOMATION_PIPE"] = "adam-auto-1",
            [Inherited.ToLowerInvariant()] = "from the automation"
        }));

        Assert.Multiple(() =>
        {
            Assert.That(entries, Does.Contain("ADAM_AUTOMATION_PIPE=adam-auto-1"));
            Assert.That(entries.Count(entry => entry.StartsWith(Inherited + "=", StringComparison.OrdinalIgnoreCase)),
                Is.EqualTo(1), "a name differing only in case is the same variable on Windows");
            Assert.That(entries, Has.Some.EndsWith("=from the automation"));
            Assert.That(entries, Has.None.EndsWith("=from the parent"));
        });
    }

    [Test]
    public void ANullValueTakesTheVariableAway()
    {
        var entries = Entries(HiddenDesktop.EnvironmentBlock(new Dictionary<string, string> { [Inherited] = null }));

        Assert.That(entries, Has.None.StartsWith(Inherited + "="));
    }

    [Test]
    public void EntriesAreSortedByName_AsTheSystemExpects()
    {
        var names = Entries(HiddenDesktop.EnvironmentBlock(new Dictionary<string, string>()))
            .Select(entry => entry[..entry.IndexOf('=', 1)])
            .ToList();

        Assert.That(names, Is.Ordered.Using((IComparer<string>)StringComparer.OrdinalIgnoreCase));
    }
}
