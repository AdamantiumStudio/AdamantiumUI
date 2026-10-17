using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// Kerning as a text property: inherited from the elements above, set for an inline by itself, and the same in a text box.
[TestFixture]
public class TextKerningTests
{
    private const string Pairs = "To LT AV";

    private static T Hosted<T>(T element) where T : Adamantium.UI.Controls.Base.UIComponent, Adamantium.UI.Core.IMeasurableComponent
    {
        var panel = new StackPanel { Children = { element } };
        var window = new Window { Width = 800, Height = 300, Content = new Border { Child = panel } };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return element;
    }

    private static double Width(TextLayout layout) => layout.GetTextData().Sum(glyph => glyph.Advance);

    private static double Laid(TextKerning kerning) =>
        Width(Hosted(new TextBlock { Text = Pairs, FontSize = 30, Kerning = kerning }).Layout);

    [Test]
    public void ATextBlock_KernsAsItIsTold()
    {
        var metrics = Laid(TextKerning.Metrics);
        var optical = Laid(TextKerning.Optical);
        var none = Laid(TextKerning.None);

        Assert.Multiple(() =>
        {
            Assert.That(metrics, Is.LessThan(none));
            Assert.That(optical, Is.LessThan(none));
            Assert.That(optical, Is.Not.EqualTo(metrics).Within(1e-3));
        });
    }

    [Test]
    public void Kerning_IsInheritedFromAnElementAbove()
    {
        var text = new TextBlock { Text = Pairs, FontSize = 30 };
        var panel = new StackPanel { Kerning = TextKerning.None, Children = { text } };
        var window = new Window { Width = 800, Height = 300, Content = panel };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        Assert.That(Width(text.Layout), Is.EqualTo(Laid(TextKerning.None)).Within(1e-6));
    }

    [Test]
    public void ChangingKerning_LaysTheTextOutAgain_OnTheBlockOrAbove()
    {
        var text = new TextBlock { Text = Pairs, FontSize = 30 };
        var panel = new StackPanel { Children = { text } };
        var window = new Window { Width = 800, Height = 300, Content = panel };
        void Update()
        {
            for (var i = 0; i < 5; i++)
            {
                WindowExtension.UpdateTree(window);
            }
        }

        Update();
        text.Kerning = TextKerning.None;
        Update();
        var own = Width(text.Layout);
        text.ClearValue(Adamantium.UI.Controls.Base.UIComponent.KerningProperty);
        Update();
        panel.Kerning = TextKerning.Optical;
        Update();

        Assert.Multiple(() =>
        {
            Assert.That(own, Is.EqualTo(Laid(TextKerning.None)).Within(1e-6));
            Assert.That(Width(text.Layout), Is.EqualTo(Laid(TextKerning.Optical)).Within(1e-6));
        });
    }

    [Test]
    public void ChangingKerning_LaysATextBoxOutAgain()
    {
        var box = new TextBox { Text = Pairs, FontSize = 30 };
        var window = new Window { Width = 800, Height = 300, Content = new StackPanel { Children = { box } } };
        void Update()
        {
            for (var i = 0; i < 5; i++)
            {
                WindowExtension.UpdateTree(window);
            }
        }

        Update();
        var before = box.CaretRect(Pairs.Length).X;
        box.Kerning = TextKerning.None;
        Update();

        Assert.That(box.CaretRect(Pairs.Length).X, Is.GreaterThan(before));
    }

    [Test]
    public void AnInline_KernsByItself()
    {
        var text = new TextBlock { FontSize = 30, Kerning = TextKerning.None };
        text.Inlines.Add(new Run { Text = "To", Kerning = TextKerning.Optical });
        text.Inlines.Add(new Run { Text = " To" });
        Hosted(text);
        var glyphs = text.Layout.GetTextData();
        var plain = Hosted(new TextBlock { Text = "To To", FontSize = 30, Kerning = TextKerning.None }).Layout.GetTextData();

        Assert.Multiple(() =>
        {
            Assert.That(glyphs[0].Advance, Is.LessThan(plain[0].Advance), "the inline's T moved its o closer");
            Assert.That(glyphs[3].Advance, Is.EqualTo(plain[3].Advance).Within(1e-6), "the rest is not kerned");
        });
    }

    [Test]
    public void ATextBox_KernsToo()
    {
        double Wide(TextKerning kerning)
        {
            var box = Hosted(new TextBox { Text = Pairs, FontSize = 30, Kerning = kerning });
            return box.CaretRect(Pairs.Length).X - box.CaretRect(0).X;
        }

        Assert.That(Wide(TextKerning.Optical), Is.LessThan(Wide(TextKerning.None)));
    }
}
