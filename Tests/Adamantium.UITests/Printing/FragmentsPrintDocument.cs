using System.IO;
using Adamantium.UI.Core.Printing;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>A line of text in black, its last word red and set in a synthesized bold.</summary>
internal sealed class FragmentsPrintDocument : IPrintDocument
{
    public string Title => "Fragments";

    public int Paginate(PrintPage page) => 1;

    public void DrawPage(int index, UI.Core.Graphics.IDrawingSession session)
    {
        var typeface = Fonts.Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "RobotoFlex-Variable.ttf"), 3);
        var layout = new Graphics.Fonts.TextLayout(typeface, typeface.GetFont(0));
        layout.ProcessText(new Graphics.Fonts.AttributedText("black red").Apply(6, 3, new Graphics.Fonts.TextAttributes
            {
                Foreground = new Mathematics.Color((byte)255, (byte)0, (byte)0, (byte)255),
                Synthesis = Fonts.FontSynthesis.Weight,
            }), 20, new Mathematics.Size<double>(double.NaN, double.NaN), Graphics.Fonts.TextWrapping.NoWrap,
            Graphics.Fonts.TextTrimming.None, Graphics.Fonts.HorizontalTextAlignment.Left,
            Graphics.Fonts.VerticalTextAlignment.Top);
        session.DrawText(new Graphics.Fonts.TextRenderingParameters(), default, layout,
            new UI.Core.Media.SolidColorBrush(new Mathematics.Color((byte)0, (byte)0, (byte)0, (byte)255)), null, null);
    }
}
