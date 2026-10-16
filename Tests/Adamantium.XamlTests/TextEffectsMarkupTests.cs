using Adamantium.Mathematics;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Text effects written in markup: the build compiles them, and the designer's loader builds the same.</summary>
[TestFixture]
public class TextEffectsMarkupTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private const string Block =
        "<TextBlock " + Namespaces + " Text=\"wave\">" +
        "<TextBlock.TextEffects>" +
        "<TextEffect PositionStart=\"1\" PositionCount=\"2\" Foreground=\"Red\" TransformOrigin=\"Element\">" +
        "<TextEffect.Transform><Transform x:Name=\"Letter\" RotationAngle=\"20\"/></TextEffect.Transform>" +
        "</TextEffect>" +
        "</TextBlock.TextEffects>" +
        "</TextBlock>";

    private const string Inline =
        "<TextBlock " + Namespaces + "><TextBlock.Inlines>" +
        "<Run Text=\"raised\"><Run.TextEffects><TextEffect PositionStart=\"2\" PositionCount=\"2\">" +
        "<TextEffect.Transform><Transform TranslateY=\"-8\"/></TextEffect.Transform>" +
        "</TextEffect></Run.TextEffects></Run>" +
        "</TextBlock.Inlines></TextBlock>";

    private const string Clipped =
        "<TextBlock " + Namespaces + " Text=\"wipe\"><TextBlock.TextEffects>" +
        "<TextEffect PositionStart=\"0\" PositionCount=\"4\" ClipFollowsTransform=\"False\">" +
        "<TextEffect.Clip><RectangleGeometry Rect=\"0,0,20,30\" CornerRadius=\"4\"/></TextEffect.Clip>" +
        "</TextEffect></TextBlock.TextEffects></TextBlock>";

    [TestCase(Block)]
    [TestCase(Inline)]
    [TestCase(Clipped)]
    public void TextEffects_Compile(string markup)
    {
        var errors = AumlCodegenHarness.Compile(
            "<Window " + Namespaces + ">" + markup.Replace(" " + Namespaces, string.Empty) + "</Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    [Test]
    public void TheDesignersLoader_BuildsABlocksEffects()
    {
        var result = AumlLoader.Load(Block);

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var effect = ((TextBlock)result.Root).TextEffects[0];
        Assert.Multiple(() =>
        {
            Assert.That((effect.PositionStart, effect.PositionCount), Is.EqualTo((1, 2)));
            Assert.That(effect.TransformOrigin, Is.EqualTo(TextEffectOrigin.Element));
            Assert.That(((SolidColorBrush)effect.Foreground).Color, Is.EqualTo(Colors.Red));
            Assert.That(effect.Transform.RotationAngle, Is.EqualTo(20));
        });
    }

    [Test]
    public void TheDesignersLoader_BuildsAnEffectsClip()
    {
        var result = AumlLoader.Load(Clipped);

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var effect = ((TextBlock)result.Root).TextEffects[0];
        Assert.Multiple(() =>
        {
            Assert.That(effect.ClipFollowsTransform, Is.False);
            Assert.That(effect.Clip.Rect, Is.EqualTo(new Adamantium.UI.Core.Rect(0, 0, 20, 30)));
            Assert.That(effect.Clip.CornerRadius.TopLeft, Is.EqualTo(4));
        });
    }

    public sealed class Data
    {
        public Brush Paint { get; set; } = new SolidColorBrush(Colors.Blue);

        public double Lift { get; set; } = -7;
    }

    private const string Bound =
        "<TextBlock " + Namespaces + " Text=\"ab\"><TextBlock.Inlines>" +
        "<Run Text=\"ab\"><Run.TextEffects>" +
        "<TextEffect PositionStart=\"0\" PositionCount=\"1\" Foreground=\"{Binding Paint}\">" +
        "<TextEffect.Transform><Transform TranslateY=\"{Binding Lift}\"/></TextEffect.Transform>" +
        "</TextEffect></Run.TextEffects></Run>" +
        "</TextBlock.Inlines></TextBlock>";

    [TestCase(true)]
    [TestCase(false)]
    public void AnEffect_BindsAgainstItsHoldersData(bool dataFirst)
    {
        var data = new Data();
        var result = AumlLoader.Load(Bound);
        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var text = (TextBlock)result.Root;
        var effect = ((Run)text.Inlines[0]).TextEffects[0];
        if (dataFirst)
        {
            text.DataContext = data;
        }
        else
        {
            effect.Transform = new Transform();
            text.DataContext = data;
            effect.Transform = AumlLoader.Load(
                "<Transform " + Namespaces + " TranslateY=\"{Binding Lift}\"/>").Root as Transform;
        }

        Assert.Multiple(() =>
        {
            Assert.That(effect.Foreground, Is.SameAs(data.Paint));
            Assert.That(effect.Transform.TranslateY, Is.EqualTo(-7));
        });
    }

    [Test]
    public void TheDesignersLoader_BuildsAnInlinesEffects()
    {
        var result = AumlLoader.Load(Inline);

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var run = (Run)((TextBlock)result.Root).Inlines[0];
        Assert.That(run.TextEffects[0].Transform.TranslateY, Is.EqualTo(-8));
    }
}
