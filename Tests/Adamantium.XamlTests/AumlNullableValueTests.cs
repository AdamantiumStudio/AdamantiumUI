using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.LanguageServer;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A nullable property is written as the type it holds, or as its null: the editor offers that type's values,
/// and the build and the editor take what the run time parses and flag what it would throw on.</summary>
[TestFixture]
public class AumlNullableValueTests
{
    private const string Root = "<Window xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\">";

    private AumlTypeModel _model;
    private CompletionEngine _engine;

    [OneTimeSetUp]
    public void BuildModel()
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

        _model = AumlTypeModel.Build(byName.Values);
        _engine = new CompletionEngine(_model);
    }

    [TestCase("ToolSelectionIndicatorPlacement", "TabIndicatorPlacement", new[] { "Inner", "Outer" })]
    [TestCase("PinnedTabsPlacement", "PinnedTabsPlacement", new[] { "SeparateRow", "SameRow" })]
    public void ANullableEnum_OffersItsTypesMembers_UnderItsTypesName(string property, string type, string[] members)
    {
        var items = Complete("<DockingArea " + property + "=\"|\"/>");

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(i => i.Label), Is.EquivalentTo(members));
            Assert.That(items.Select(i => i.Detail), Is.All.EqualTo(type));
        });
    }

    [Test]
    public void AValueTheTypeHasNot_IsFlagged_ByTheBuildThePreviewAndTheEditor()
    {
        const string body = "<DockingArea ToolSelectionIndicatorPlacement=\"Sideways\"/>";

        Assert.Multiple(() =>
        {
            Assert.That(BuildErrors(body), Has.Some.Contains("'Sideways' is not a valid TabIndicatorPlacement"));
            Assert.That(PreviewErrors(body), Has.Some.Contains("'Sideways' is not a valid TabIndicatorPlacement"));
            Assert.That(EditorErrors(body), Has.Some.Contains("'Sideways' is not a valid TabIndicatorPlacement"));
        });
    }

    [TestCase("Outer")]
    [TestCase("outer")]
    [TestCase(" Inner ")]
    [TestCase("Outer, Inner")]
    [TestCase("null")]
    [TestCase("Null")]
    [TestCase("")]
    public void WhatTheRunTimeParses_IsTaken_ByTheBuildAndTheEditor(string value)
    {
        var body = "<DockingArea ToolSelectionIndicatorPlacement=\"" + value + "\"/>";

        Assert.Multiple(() =>
        {
            Assert.That(BuildErrors(body), Is.Empty);
            Assert.That(EditorErrors(body), Is.Empty);
        });
    }

    [TestCase("<ToggleButton IsChecked=\"yes\"/>", "'yes' is not a valid")]
    [TestCase("<Grid><Grid.Styles><Style Selector=\"ToggleButton\"><Setter Property=\"IsChecked\" Value=\"maybe\"/></Style></Grid.Styles></Grid>", "'maybe' is not a valid")]
    [TestCase("<Grid><Grid.Styles><Style Selector=\"DockingArea\"><Setter Property=\"ToolSelectionIndicatorPlacement\" Value=\"Sideways\"/></Style></Grid.Styles></Grid>", "'Sideways' is not a valid TabIndicatorPlacement")]
    public void ANullableBoolOrEnum_IsChecked_InAnAttributeAndASetter(string body, string expected) =>
        Assert.That(BuildErrors(body), Has.Some.Contains(expected));

    [TestCase("<ToggleButton IsChecked=\"True\"/>")]
    [TestCase("<ToggleButton IsChecked=\"null\"/>")]
    [TestCase("<Grid><Grid.Styles><Style Selector=\"ToggleButton\"><Setter Property=\"IsChecked\" Value=\"false\"/></Style></Grid.Styles></Grid>")]
    [TestCase("<Grid><Grid.Styles><Style Selector=\"DockingArea\"><Setter Property=\"ToolSelectionIndicatorPlacement\" Value=\"Outer|Inner\"/></Style></Grid.Styles></Grid>")]
    [TestCase("<DockingArea SelectionIndicatorThickness=\"1,000\"/>")]
    public void WhatTheRunTimeParses_IsTaken_InAnAttributeAndASetter(string body) =>
        Assert.That(BuildErrors(body), Is.Empty);

    private List<AumlCompletionItem> Complete(string marked)
    {
        var text = Root + marked + "</Window>";
        var caret = text.IndexOf('|');
        return _engine.Complete(text.Remove(caret, 1), caret, "Views/Main.auml").ToList();
    }

    private static List<string> BuildErrors(string body) =>
        AumlCodegenHarness.Diagnose("MainWindow.auml", AumlCodegenHarness.WindowHeader + ">" + body + "</Window>", compile: false)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();

    private static List<string> PreviewErrors(string body) =>
        AumlLoader.Load(AumlCodegenHarness.WindowHeader + ">" + body + "</Window>").Diagnostics.ToList();

    private List<string> EditorErrors(string body) =>
        AumlValidator.Validate(Root + body + "</Window>", _model)
            .Select(d => d.Message)
            .ToList();
}
