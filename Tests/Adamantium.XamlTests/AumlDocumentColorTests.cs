using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The colors a markup file writes - a brush or color property's value, a setter's - read as the framework reads
/// them, and the color a completion item stands for.</summary>
[TestFixture]
public class AumlDocumentColorTests
{
    private const string Root = """<Window xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions">""";

    private AumlTypeModel _model;

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

        _model = AumlTypeModel.Build(byName.Values, []);
    }

    [Test]
    public void TheColorsOfBrushAndColorProperties_AreFound_WhereTheyAreWritten()
    {
        var text = Root + """
            <Border Background="Tomato" BorderBrush="#80FF0000" Tag="Red">
                <TextBlock Text="Blue" Foreground="#00FF00"/>
                <Border.Resources/>
            </Border>
            <SolidColorBrush Color="black"/>
            </Window>
            """;

        var found = new DocumentColorEngine(_model).Find(text)
            .Select(color => (text.Substring(color.Start, color.Length), AumlColors.Hex(color.Color))).ToList();

        Assert.That(found, Is.EqualTo(new[]
        {
            ("Tomato", "#FF6347"), ("#80FF0000", "#80FF0000"), ("#00FF00", "#00FF00"), ("black", "#000000")
        }), "Tag and Text are no colors, though they say one");
    }

    [Test]
    public void ASettersColor_IsFound_ByItsStylesType()
    {
        var text = Root + """
            <Style Selector="Button.Accent:pointerover">
                <Setter Property="Background" Value="Gold"/>
                <Setter Property="Content" Value="Gold"/>
            </Style>
            </Window>
            """;

        var found = new DocumentColorEngine(_model).Find(text);

        Assert.That(found.Select(color => color.Start),
            Is.EqualTo(new[] { text.IndexOf("Value=\"Gold\"", StringComparison.Ordinal) + 7 }), "the Background setter's, not Content's");
    }

    [Test]
    public void WhatIsNoColor_IsNotOne()
    {
        var text = Root + """<Border Background="{Binding Fill}" BorderBrush="#XYZ" Foreground="Nonsense"/></Window>""";

        Assert.That(new DocumentColorEngine(_model).Find(text), Is.Empty);
    }

    [Test]
    public void AColorInTheCompletionList_SaysWhichColorItIs()
    {
        var text = Root + """<Border Background="|"/></Window>""";
        var caret = text.IndexOf('|');

        var items = new CompletionEngine(_model).Complete(text.Remove(caret, 1), caret);

        Assert.That(items.Single(item => item.Label == "Red").Color, Is.EqualTo("#FF0000"));
        Assert.That(items.Single(item => item.Label == "Green").Color, Is.EqualTo("#008000"), "the framework's green, as CSS's");
    }
}
