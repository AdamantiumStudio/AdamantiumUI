using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The language server knows the class a <c>&lt;Pane&gt;</c> file becomes, as it knows a view's.</summary>
[TestFixture]
public class AumlPaneFileLanguageServerTests
{
    private string _project;
    private AumlTypeModel _model;

    [OneTimeSetUp]
    public void BuildModel()
    {
        _project = Path.Combine(Path.GetTempPath(), $"aumlpane-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_project, "Panes"));
        var pane = Path.Combine(_project, "Panes", "InspectorPane.auml");
        File.WriteAllText(pane, """<Pane xmlns="http://adamantium/ui" Header="Inspector" Kind="Tool" Zone="Right"><Grid/></Pane>""");

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll")) byName[Path.GetFileName(dll)] = dll;
        foreach (var dll in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
            byName.TryAdd(Path.GetFileName(dll), dll);

        _model = AumlTypeModel.Build(byName.Values, []);
        _model.RegisterViews([pane], "Probe.App", _project);
    }

    [OneTimeTearDown]
    public void RemoveProject() => Directory.Delete(_project, recursive: true);

    [Test]
    public void APaneFile_IsAKnownElement() =>
        Assert.That(_model.GetElement("clr-namespace:Probe.App.Panes", "InspectorPane"), Is.Not.Null);

    [Test]
    public void APaneFile_PlacedInADockingArea_IsNotFlagged()
    {
        const string window =
            """<Window xmlns="http://adamantium/ui" xmlns:panes="clr-namespace:Probe.App.Panes">""" +
            """<DockingArea><panes:InspectorPane/></DockingArea></Window>""";

        Assert.That(AumlValidator.Validate(window, _model).Where(d => d.Code == "AUM001"), Is.Empty);
    }
}
