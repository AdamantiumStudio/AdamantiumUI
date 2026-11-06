using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A palette with a routed command: it shows the color picked last, as a split button's does, or - asked to -
/// the color the command answers, as a card's palette shows what a pick changes.</summary>
[TestFixture]
public class ColorPaletteCommandTests
{
    private static readonly RoutedCommand LineColor = new("LineColor", typeof(ColorPaletteCommandTests));

    [TearDown]
    public void LetGoOfTheFocus() => FocusManager.ResetFocus();

    private static ColorPalette PaletteInAnEditorAnswering(Color? color, bool showsCommandColor)
    {
        var palette = new ColorPalette { ShowsCommandColor = showsCommandColor, SelectedColor = Colors.Blue };
        var editor = new StackPanel { Children = { palette } };
        CommandManager.GetCommandBindings(editor).Add(new CommandBinding(LineColor, (_, _) => { }, (_, e) =>
        {
            e.CanExecute = true;
            e.Value = color;
        }));
        palette.Command = LineColor;
        return palette;
    }

    [Test]
    public void AskedTo_ItShowsTheColorTheCommandAnswers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PaletteInAnEditorAnswering(Colors.Red, true).SelectedColor, Is.EqualTo(Colors.Red));
            Assert.That(PaletteInAnEditorAnswering(null, true).SelectedColor, Is.Null, "no color, the automatic");
        });
    }

    [Test]
    public void Otherwise_ItKeepsTheColorPickedLast()
    {
        Assert.That(PaletteInAnEditorAnswering(Colors.Red, false).SelectedColor, Is.EqualTo(Colors.Blue));
    }
}
