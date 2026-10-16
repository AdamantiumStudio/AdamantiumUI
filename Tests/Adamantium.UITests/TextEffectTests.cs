using System.Collections.Generic;
using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Extensions;
using Adamantium.UI.Rendering;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

// A text effect draws a range of a text block apart from the rest; the block hands the ranges to the renderer as glyph
// effects, in the layout's characters, and lays nothing out again for them.
[TestFixture]
public class TextEffectTests
{
    private sealed class Recorder : IDrawingContext
    {
        public RecordingDrawingSession Session { get; } = new();

        public IDrawingSession ForControl(IUIComponent component) => Session;
    }

    private static TextBlock Hosted(TextBlock text)
    {
        var window = new Window { Width = 600, Height = 300, Content = new Border { Child = text } };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return text;
    }

    private static IReadOnlyList<GlyphEffect> Drawn(TextBlock text)
    {
        var recorder = new Recorder();
        text.Render(recorder);
        return recorder.Session.Effects.LastOrDefault() ?? [];
    }

    private static TextEffect Raise(int start, int count) => new()
    {
        PositionStart = start,
        PositionCount = count,
        Transform = new Transform { TranslateY = -5 },
    };

    [Test]
    public void ABlocksEffect_CountsTheBlocksText()
    {
        var text = new TextBlock { Text = "hello world" };
        text.TextEffects.Add(Raise(6, 5));
        Hosted(text);

        var effect = Drawn(text).Single();

        Assert.Multiple(() =>
        {
            Assert.That((effect.Start, effect.Length), Is.EqualTo((6, 5)));
            Assert.That(effect.AroundGlyph, Is.True, "about each character by default");
            Assert.That(effect.Transform, Is.EqualTo(GlyphEffect.Flat(Matrix3x2F.Translation(0, -5))));
        });
    }

    [Test]
    public void AnInlinesEffect_CountsFromTheInline_AndStopsAtItsEnd()
    {
        var text = new TextBlock();
        var first = new Run { Text = "abc" };
        var second = new Run { Text = "defg" };
        first.TextEffects.Add(Raise(1, 10));
        second.TextEffects.Add(Raise(1, 2));
        text.Inlines.Add(first);
        text.Inlines.Add(second);
        Hosted(text);

        var ranges = Drawn(text).Select(effect => (effect.Start, effect.Length)).ToList();

        Assert.That(ranges, Is.EquivalentTo(new[] { (1, 2), (4, 2) }));
    }

    [Test]
    public void AnEffectInASpan_CountsFromTheSpan()
    {
        var text = new TextBlock();
        var span = new Span();
        span.Inlines.Add(new Run { Text = "xy" });
        span.TextEffects.Add(Raise(1, 1));
        text.Inlines.Add(new Run { Text = "ab" });
        text.Inlines.Add(span);
        Hosted(text);

        var effect = Drawn(text).Single();

        Assert.That((effect.Start, effect.Length), Is.EqualTo((3, 1)));
    }

    [Test]
    public void NoEffects_OrOnesThatChangeNothing_DrawThePlainText()
    {
        var text = new TextBlock { Text = "plain" };
        text.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 3 });
        text.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 0, Transform = new Transform { TranslateX = 3 } });
        Hosted(text);

        Assert.That(Drawn(text), Is.Empty);
    }

    [Test]
    public void AForeground_RecolorsTheRange()
    {
        var text = new TextBlock { Text = "red" };
        text.TextEffects.Add(new TextEffect
        {
            PositionStart = 0,
            PositionCount = 3,
            Foreground = new SolidColorBrush(Colors.Red),
        });
        Hosted(text);

        var effect = Drawn(text).Single();

        Assert.That(effect.Color, Is.EqualTo(Colors.Red.ToVector4()));
    }

    [Test]
    public void TheElementsOrigin_TurnsAboutTheElement()
    {
        var text = new TextBlock { Text = "swing" };
        text.TextEffects.Add(new TextEffect
        {
            PositionStart = 0,
            PositionCount = 5,
            TransformOrigin = TextEffectOrigin.Element,
            Transform = new Transform { RotationAngle = 10 },
        });
        Hosted(text);

        var effect = Drawn(text).Single();
        var m = effect.Transform;
        var turned = new Vector2F(100 * m.M11 + m.M31, 100 * m.M12 + m.M32);

        Assert.Multiple(() =>
        {
            Assert.That(effect.AroundGlyph, Is.False);
            Assert.That(turned.Y, Is.GreaterThan(10), "a point far along the line swings down about the corner");
        });
    }

    [Test]
    public void ChangingAnEffect_DrawsAgain_WithoutLayingOut()
    {
        var text = new TextBlock();
        var run = new Run { Text = "abc" };
        var effect = Raise(0, 1);
        run.TextEffects.Add(effect);
        text.Inlines.Add(run);
        Hosted(text);
        var before = Drawn(text);
        var layout = text.Layout.Revision;

        effect.Transform.TranslateY = -9;
        var after = Drawn(text);

        Assert.Multiple(() =>
        {
            Assert.That(text.IsMeasureValid, Is.True, "nothing to lay out");
            Assert.That(text.Layout.Revision, Is.EqualTo(layout));
            Assert.That(after.Single().Transform, Is.EqualTo(GlyphEffect.Flat(Matrix3x2F.Translation(0, -9))));
            Assert.That(after, Is.Not.SameAs(before));
        });
    }

    [Test]
    public void StillEffects_AreDrawnFromTheSameSnapshot()
    {
        var text = new TextBlock { Text = "still" };
        text.TextEffects.Add(Raise(0, 2));
        Hosted(text);

        var first = Drawn(text);
        text.InvalidateRender(false);
        var second = Drawn(text);

        Assert.That(first, Has.Count.EqualTo(1));
        Assert.That(second, Is.SameAs(first), "nothing changed, nothing allocated");
    }

    [Test]
    public void AnimatingAnEffectsTransform_StaysOnTheLoop_AndMovesNotTheBlock()
    {
        var text = new TextBlock { Text = "wave" };
        var effect = Raise(0, 1);
        text.TextEffects.Add(effect);
        Hosted(text);

        effect.Transform.BeginAnimation(Transform.TranslateYProperty,
            new DoubleAnimation { From = 0, To = -10, Duration = System.TimeSpan.FromSeconds(1) });

        Assert.That(text.IsRenderMotionNode, Is.False, "the block is not handed to the compositor");
    }

    [Test]
    public void AShiftAboutEachCharacter_CountsInCharacterSizes()
    {
        var text = new TextBlock { Text = "abc" };
        text.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 3, Transform = new Transform { ShiftY = -1 } });
        Hosted(text);

        var effect = Drawn(text).Single();

        Assert.Multiple(() =>
        {
            Assert.That(effect.Shift, Is.EqualTo(new Vector2F(0, -1)));
            Assert.That(effect.Transform.IsIdentity, Is.True);
        });
    }

    [Test]
    public void AShiftInTheElementsSpace_CountsInTheElementsSize()
    {
        var text = new TextBlock { Text = "abc" };
        text.TextEffects.Add(new TextEffect
        {
            PositionStart = 0,
            PositionCount = 3,
            TransformOrigin = TextEffectOrigin.Element,
            Transform = new Transform { ShiftX = 1 },
        });
        Hosted(text);

        var effect = Drawn(text).Single();

        Assert.Multiple(() =>
        {
            Assert.That(effect.Shift, Is.EqualTo(default(Vector2F)));
            Assert.That(effect.Transform.M31, Is.EqualTo(text.RenderSize.Width).Within(1e-3));
        });
    }

    [Test]
    public void ChangingARunsBrush_LaysTheTextOutAgain()
    {
        var foreground = new SolidColorBrush(Colors.Red);
        var line = new SolidColorBrush(Colors.Green);
        var text = new TextBlock();
        var run = new Run { Text = "abc", Foreground = foreground };
        run.DecorationLines.Add(new TextDecoration { Brush = line });
        text.Inlines.Add(run);
        var window = new Window { Width = 300, Height = 200, Content = new Border { Child = text } };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        foreground.Color = Colors.Blue;
        var afterText = text.IsMeasureValid;
        WindowExtension.UpdateTree(window);
        line.Color = Colors.Yellow;

        Assert.Multiple(() =>
        {
            Assert.That(afterText, Is.False, "a run's color is part of its laid-out text");
            Assert.That(text.IsMeasureValid, Is.False, "and so is a line's");
        });
    }

    [Test]
    public void ABrushesOpacity_FadesTheRange()
    {
        var brush = new SolidColorBrush(Colors.Red) { Opacity = 0.25 };
        var text = new TextBlock { Text = "abc" };
        text.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 3, Foreground = brush });
        Hosted(text);

        Assert.That(Drawn(text).Single().Color!.Value.W, Is.EqualTo(0.25f).Within(1e-4));
    }

    [Test]
    public void ABrushTextIsDrawnWith_IsAnimatedOnTheLoop_NotByTheCompositor()
    {
        var effectBrush = new SolidColorBrush(Colors.Red);
        var runBrush = new SolidColorBrush(Colors.Blue);
        var effect = new TextEffect { PositionStart = 0, PositionCount = 1, Foreground = effectBrush };
        var run = new Run { Text = "abc", Foreground = runBrush };
        var text = new TextBlock();
        text.Inlines.Add(run);
        text.TextEffects.Add(effect);

        Assert.Multiple(() =>
        {
            Assert.That(effectBrush.IsBakedIn, Is.True);
            Assert.That(runBrush.IsBakedIn, Is.True);
            var curve = new AnimationCurve([new AnimationCurve.Track(Brush.OpacityProperty, [0, 1], [0, 1])], 1, 0,
                double.PositiveInfinity, false, null);
            Assert.That(Compositor.TryTakeOver(effectBrush, curve), Is.Null, "the effect would never see the frames");
        });

        effect.Foreground = null;
        run.Foreground = null;

        Assert.That(effectBrush.IsBakedIn || runBrush.IsBakedIn, Is.False, "let go with the brush");
    }

    [Test]
    public void ABrushIsBakedIn_OnlyWhileItsTextIsInABlock()
    {
        var shared = new SolidColorBrush(Colors.Red);
        var line = new TextDecoration { Brush = new SolidColorBrush(Colors.Green) };
        var text = new TextBlock();
        var first = new Run { Text = "abc", Foreground = shared };
        var second = new Run { Text = "def" };
        first.DecorationLines.Add(line);
        second.DecorationLines.Add(line);
        var effect = new TextEffect { PositionStart = 0, PositionCount = 1, Foreground = new SolidColorBrush(Colors.Blue) };
        Assume.That(shared.IsBakedIn || line.Brush.IsBakedIn || effect.Foreground.IsBakedIn, Is.False, "not in a block yet");

        text.Inlines.Add(first);
        text.Inlines.Add(second);
        text.TextEffects.Add(effect);
        Assume.That(shared.IsBakedIn && line.Brush.IsBakedIn && effect.Foreground.IsBakedIn, Is.True);

        first.DecorationLines.Remove(line);
        var lineStillBaked = line.Brush.IsBakedIn;
        text.Inlines.Clear();
        text.TextEffects.Remove(effect);

        Assert.Multiple(() =>
        {
            Assert.That(lineStillBaked, Is.True, "the other run still draws with the line");
            Assert.That(shared.IsBakedIn, Is.False, "the run left the block");
            Assert.That(line.Brush.IsBakedIn, Is.False);
            Assert.That(effect.Foreground.IsBakedIn, Is.False, "the effect left the block");
        });
    }

    [Test]
    public void TextMovedToAnotherBlock_AddedThereFirst_KeepsItsBrushBakedIn()
    {
        var runBrush = new SolidColorBrush(Colors.Red);
        var effectBrush = new SolidColorBrush(Colors.Blue);
        var a = new TextBlock();
        var b = new TextBlock();
        var run = new Run { Text = "abc", Foreground = runBrush };
        var effect = new TextEffect { PositionStart = 0, PositionCount = 1, Foreground = effectBrush };
        a.Inlines.Add(run);
        a.TextEffects.Add(effect);

        b.Inlines.Add(run);
        b.TextEffects.Add(effect);
        a.Inlines.Remove(run);
        a.TextEffects.Remove(effect);

        Assert.Multiple(() =>
        {
            Assert.That(runBrush.IsBakedIn, Is.True);
            Assert.That(effectBrush.IsBakedIn, Is.True);
        });
    }

    [Test]
    public void ABrushAnimationTheCompositorPlays_ComesBackWhenTextBakesTheBrush()
    {
        var brush = new SolidColorBrush(Colors.Red);
        var curve = new AnimationCurve([new AnimationCurve.Track(Brush.OpacityProperty, [0, 1], [0, 1])], 1, 0,
            double.PositiveInfinity, false, null);
        var animation = new RunningKeyFrameAnimation(brush, curve, FillBehavior.HoldEnd, true, null);
        Assume.That(animation.IsComposited, Is.True, "the compositor took it");
        var text = new TextBlock { Text = "abc" };

        text.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Foreground = brush });
        animation.Advance(0.016);

        Assert.That(animation.IsComposited, Is.False, "played on the loop, where the text sees every value");
        animation.ReleaseComposited();
    }

    [Test]
    public void APerspectiveTurn_TipsTheCharacters()
    {
        var text = new TextBlock { Text = "abc" };
        text.TextEffects.Add(new TextEffect
        {
            PositionStart = 0,
            PositionCount = 3,
            Transform = new Transform { RotationY = 40, Perspective = 200 },
        });
        Hosted(text);

        var effect = Drawn(text).Single();

        Assert.That(effect.Transform.M13, Is.Not.EqualTo(0).Within(1e-6), "a perspective, not a flat squash");
    }

    [Test]
    public void ARangeStartingBeforeTheText_KeepsItsEnd()
    {
        var text = new TextBlock { Text = "abcdefgh" };
        text.TextEffects.Add(Raise(-5, 6));
        Hosted(text);

        var effect = Drawn(text).Single();

        Assert.That((effect.Start, effect.Length), Is.EqualTo((0, 1)));
    }

    [Test]
    public void ATransformAlsoMovingAnElement_StillMovesIt()
    {
        var shared = new Transform();
        var moved = new Border { Width = 20, Height = 20, RenderTransform = shared };
        var text = new TextBlock { Text = "abc" };
        var effect = new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared };
        text.TextEffects.Add(effect);
        var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { moved, text } } };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        RenderDirty.Clear();
        shared.TranslateX = 20;

        Assert.Multiple(() =>
        {
            Assert.That(RenderDirty.IsTransform || RenderDirty.MovedNodes.Contains(moved), Is.True, "the element moves");
            Assert.That(shared.InheritanceParent, Is.SameAs(moved), "its bindings still find the element's data");
            Assert.That(Drawn(text).Single().Transform, Is.EqualTo(GlyphEffect.Flat(Matrix3x2F.Translation(20, 0))),
                "and the effect follows");
        });
    }

    [Test]
    public void ATransformTwoEffectsShare_StaysHeld_WhenOneLetsGo()
    {
        var shared = new Transform();
        var first = new TextBlock { Text = "abc" };
        var second = new TextBlock { Text = "def" };
        var letGo = new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared };
        first.TextEffects.Add(letGo);
        second.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });
        Hosted(first);
        Hosted(second);

        letGo.Transform = new Transform();
        RenderDirty.Clear();
        shared.TranslateY = -3;

        Assert.Multiple(() =>
        {
            Assert.That(RenderDirty.IsTransformUnknown, Is.False, "no re-capture of the whole scene");
            Assert.That(Drawn(second).Single().Transform, Is.EqualTo(GlyphEffect.Flat(Matrix3x2F.Translation(0, -3))));
        });
    }

    [Test]
    public void ChangingTheEffectsBrush_DrawsTheTextAgain()
    {
        var brush = new SolidColorBrush(Colors.Red);
        var text = new TextBlock { Text = "abc" };
        text.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 3, Foreground = brush });
        Hosted(text);
        Drawn(text);

        brush.Color = Colors.Blue;

        Assert.That(text.IsGeometryValid, Is.False, "the block is to be drawn again");
        Assert.That(Drawn(text).Single().Color, Is.EqualTo(Colors.Blue.ToVector4()));
    }

    [Test]
    public void ATransformTwoBlocksShare_PassesToTheOther_WhenOneIsDiscarded()
    {
        var shared = new Transform();
        var first = new TextBlock { Text = "abc" };
        var second = new TextBlock { Text = "def" };
        first.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });
        second.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });
        Hosted(first);
        Hosted(second);

        DiscardedVisuals.Publish(first);
        while (DiscardedVisuals.Drain(int.MaxValue) > 0)
        {
        }

        Assert.That(shared.Element, Is.SameAs(second));
    }

    [Test]
    public void ATransformGivenToAnotherEffect_FollowsIt()
    {
        var shared = new Transform();
        var first = new TextBlock { Text = "abc" };
        var second = new TextBlock { Text = "def" };
        var removed = new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared };
        first.TextEffects.Add(removed);
        Hosted(first);
        Hosted(second);

        first.TextEffects.Remove(removed);
        second.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });

        Assert.That(shared.Element, Is.SameAs(second));
    }

    [Test]
    public void ATransformInARemovedInline_PassesToTheBlockStillShowingIt()
    {
        var shared = new Transform();
        var first = new TextBlock();
        var run = new Run { Text = "abc" };
        run.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });
        first.Inlines.Add(run);
        var second = new TextBlock { Text = "def" };
        second.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });
        Hosted(first);
        Hosted(second);
        Assume.That(shared.Element, Is.SameAs(first));

        first.Inlines.Remove(run);

        Assert.That(shared.Element, Is.SameAs(second));
    }

    [Test]
    public void ATransformSomethingElseHangs_StaysWhereItHangs()
    {
        var shared = new Transform();
        var drawnIn = new Border();
        shared.InheritanceParent = drawnIn;
        var text = new TextBlock { Text = "abc" };

        text.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });

        Assert.That(shared.InheritanceParent, Is.SameAs(drawnIn));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ATransformADrawingLetsGo_PassesToTheEffectHoldingIt(bool drawingFirst)
    {
        var shared = new Transform();
        var group = new Adamantium.UI.Core.Media.Drawings.DrawingGroup();
        var text = new TextBlock { Text = "abc" };
        if (drawingFirst)
        {
            group.Transform = shared;
            group.Attach(new Border());
        }

        text.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });
        Hosted(text);
        if (!drawingFirst)
        {
            group.Transform = shared;
            group.Attach(new Border());
        }

        group.Transform = null;

        Assert.That(shared.Element, Is.SameAs(text));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AGeometrysTransformTheDrawingLetsGo_PassesToTheEffectHoldingIt(bool dropGeometry)
    {
        var shared = new Transform();
        var geometry = new RectangleGeometry { Transform = shared };
        var drawing = new Adamantium.UI.Core.Media.Drawings.GeometryDrawing { Geometry = geometry };
        drawing.Attach(new Border());
        var text = new TextBlock { Text = "abc" };
        text.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });
        Hosted(text);

        if (dropGeometry)
        {
            drawing.Geometry = null;
        }
        else
        {
            geometry.Transform = null;
        }

        Assert.That(shared.Element, Is.SameAs(text));
    }

    [Test]
    public void ATransformLeftOnADrawingNoLongerShown_PassesToTheEffectHoldingIt()
    {
        var shared = new Transform();
        var image = new Adamantium.UI.Core.Media.Imaging.DrawingImage
        {
            Drawing = new Adamantium.UI.Core.Media.Drawings.DrawingGroup { Transform = shared },
        };
        var shownIn = new Border();
        image.Attach(shownIn);
        var text = new TextBlock { Text = "abc" };
        text.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });
        Hosted(text);

        image.Detach(shownIn);

        Assert.That(shared.Element, Is.SameAs(text));
    }

    [Test]
    public void ATransformADrawingLetsGo_ReturnsToTheElementItMoves()
    {
        var shared = new Transform();
        var moved = new Border { RenderTransform = shared };
        var group = new Adamantium.UI.Core.Media.Drawings.DrawingGroup { Transform = shared };
        group.Attach(new Border());

        group.Transform = null;

        Assert.That(shared.InheritanceParent, Is.SameAs(moved));
    }

    [Test]
    public void ATransformNoLongerMovingAnElement_FollowsItsEffect()
    {
        var shared = new Transform();
        var moved = new Border { Width = 20, Height = 20, RenderTransform = shared };
        var text = new TextBlock { Text = "abc" };
        text.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });
        var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { moved, text } } };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        moved.RenderTransform = null;

        Assert.That(shared.Element, Is.SameAs(text));
    }

    [Test]
    public void ALongLivedTransform_LetsGoOfADiscardedBlock()
    {
        var shared = new Transform();
        var text = new TextBlock();
        var run = new Run { Text = "abc" };
        run.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 1, Transform = shared });
        text.TextEffects.Add(new TextEffect { PositionStart = 1, PositionCount = 1, Transform = shared });
        text.Inlines.Add(run);
        Hosted(text);
        Assume.That(shared.IsHeld, Is.True);

        DiscardedVisuals.Publish(text);
        while (DiscardedVisuals.Drain(int.MaxValue) > 0)
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(shared.IsHeld, Is.False);
            Assert.That(shared.InheritanceParent, Is.Null, "nothing of the block is reachable from it");
        });
    }

    [Test]
    public void AnEffectsAnimation_EndsWithItsElementsSubtree()
    {
        var text = new TextBlock { Text = "wave" };
        var effect = Raise(0, 1);
        text.TextEffects.Add(effect);
        var window = new Window { Width = 300, Height = 200, Content = new Border { Child = text } };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        var before = AnimationManager.ActiveCount;
        effect.Transform.BeginAnimation(Transform.TranslateYProperty, new DoubleAnimation
        {
            From = 0,
            To = -10,
            Duration = System.TimeSpan.FromSeconds(1),
            IterationCount = double.PositiveInfinity,
        });
        Assume.That(AnimationManager.ActiveCount, Is.EqualTo(before + 1));

        AnimationManager.CancelSubtree(window);

        Assert.That(AnimationManager.ActiveCount, Is.EqualTo(before));
    }

    [Test]
    public void TheResolver_GivesEachCharacterAnEffectOfItsOwn()
    {
        var template = new TextEffect
        {
            Foreground = new SolidColorBrush(Colors.Red),
            Transform = new Transform { RotationAngle = 30 },
        };
        var effects = new List<TextEffect>();

        TextEffectResolver.Resolve(template, 2, 3, effects);

        Assert.Multiple(() =>
        {
            Assert.That(effects.Select(e => (e.PositionStart, e.PositionCount)), Is.EqualTo(new[] { (2, 1), (3, 1), (4, 1) }));
            Assert.That(effects.Select(e => e.Transform).Distinct().Count(), Is.EqualTo(3), "each its own transform");
            Assert.That(effects.Select(e => e.Transform.RotationAngle), Is.All.EqualTo(30));
            Assert.That(effects.Select(e => e.Foreground), Is.All.SameAs(template.Foreground));
        });
    }

    [Test]
    public void TheBatch_MovesAGlyphInTheNodeAsTheLayoutSaid()
    {
        // Far from the node's origin, as a block low on a page: w at the node's origin is then negative, and the w a
        // corner gets must still be the one it has in the layout - its sign is what the rasterizer clips by.
        float sx = 2, sy = 3, tx = 180, ty = 815, ax = 1, ay = 2;
        var flat = Matrix3x2F.Rotation(0.3f) * Matrix3x2F.Scaling(1.2f, 0.8f) * Matrix3x2F.Translation(4, -2);
        var layoutEffect = GlyphEffect.Flat(flat);
        layoutEffect.M13 = 0.004f;
        layoutEffect.M23 = 0.004f;
        var glyph = new FontItem
        {
            Effect = new Vector4F(layoutEffect.M11 - 1, layoutEffect.M12, layoutEffect.M21, layoutEffect.M22 - 1),
            EffectOffset = new Vector2F(layoutEffect.M31, layoutEffect.M32),
            EffectPerspective = new Vector4F(layoutEffect.M13, layoutEffect.M23, 0, 0),
        };
        var q = new Vector2F(10, 20);
        var p = new Vector2F((q.X + ax) * sx + tx, (q.Y + ay) * sy + ty);
        var w = 1 + q.X * layoutEffect.M13 + q.Y * layoutEffect.M23;
        var moved = new Vector2F((q.X * layoutEffect.M11 + q.Y * layoutEffect.M21 + layoutEffect.M31) / w,
            (q.X * layoutEffect.M12 + q.Y * layoutEffect.M22 + layoutEffect.M32) / w);
        var expected = new Vector2F((moved.X + ax) * sx + tx, (moved.Y + ay) * sy + ty);

        var (effect, offset, perspective) = TextBatchCollector.NodeEffect(glyph, sx, sy, tx, ty, ax, ay);
        var nodeW = 1 + perspective.Z + p.X * perspective.X + p.Y * perspective.Y;
        var drawn = new Vector2F(
            (p.X + p.X * effect.X + p.Y * effect.Z + offset.X) / nodeW,
            (p.Y + p.X * effect.Y + p.Y * effect.W + offset.Y) / nodeW);

        Assert.Multiple(() =>
        {
            Assert.That(1 + perspective.Z, Is.LessThan(0), "the case that clipped: w at the node's origin below zero");
            Assert.That(nodeW, Is.EqualTo(w).Within(1e-3), "the corner's w as in the layout, sign and all");
            Assert.That(drawn.X, Is.EqualTo(expected.X).Within(1e-2));
            Assert.That(drawn.Y, Is.EqualTo(expected.Y).Within(1e-2));
        });
    }
}
