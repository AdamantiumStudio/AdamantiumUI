using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Adamantium.UI.Automation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Sandbox.ModuleLoading;
using Adamantium.UI.Sandbox.Resources;
using Adamantium.UI.Sandbox.ViewModels;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>A ribbon button names the shell's command by <c>Ribbon.QuickAccessKey</c>, and asking it into the bar finds
/// the command by that key. A key the catalog does not know makes "add to the bar" do nothing, and says nothing.</summary>
[TestFixture]
public class RibbonShellQuickAccessKeyTests
{
    [Test]
    public void EveryKeyTheRibbonWrites_NamesACommandOfTheCatalog()
    {
        var app = HeadlessApplication.Start<Fluent>();
        app.ResourceManager.AddSource(new ModuleResources(), typeof(RibbonShellIcons), ResourceScope.Global);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(dir.FullName, "Adamantium.UI.Sandbox", "Views")))
        {
            dir = dir.Parent;
        }

        var markup = XDocument.Load(Path.Combine(dir.FullName, "Adamantium.UI.Sandbox", "Views", "RibbonShellView.auml"));
        var written = markup.Descendants()
            .Select(element => element.Attribute("Ribbon.QuickAccessKey")?.Value)
            .Where(key => key != null && !key.StartsWith('{'))
            .Distinct()
            .ToList();
        var known = new RibbonShellViewModel().Commands.All.Select(command => command.Key).ToHashSet();

        Assume.That(written, Is.Not.Empty, "the ribbon names its commands by key");
        Assert.That(written.Where(key => !known.Contains(key)), Is.Empty, "keys the catalog does not know");
    }
}
