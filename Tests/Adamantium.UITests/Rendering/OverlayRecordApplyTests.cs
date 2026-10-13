using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>
/// An overlay stage (popups, adorners) records on the thread that lays the tree out and applies on the thread that draws.
/// What it applies is what was recorded: a list rebuilt between the two - the loop already handling the next click while
/// the render thread draws - must not show up half laid out in the frame being drawn.
/// </summary>
[TestFixture]
public class OverlayRecordApplyTests
{
    private TestRoot _root;
    private FakeRenderUnitFactory _factory;
    private RenderCache _cache;

    [SetUp]
    public void SetUp()
    {
        _root = new TestRoot();
        _factory = new FakeRenderUnitFactory();
        _cache = new RenderCache(new DrawingContext(), _factory);
    }

    private Border AddControlAt(double x)
    {
        var control = new Border { Width = 10, Height = 10, Background = Brushes.Red };
        _root.Add(control);
        control.Measure(new Size(10, 10));
        control.Arrange(new Rect(x, 0, 10, 10));
        return control;
    }

    private bool Apply()
    {
        var applied = _cache.ApplyComponents();
        if (applied) _cache.ProcessCommands(_cache.AppliedProjection, 1.0);
        return applied;
    }

    private FakeRenderUnit LiveUnitOf(IUIComponent component) =>
        _factory.Created.LastOrDefault(unit => ReferenceEquals(unit.Component, component) && unit.DeferDisposeCount == 0
                                               && unit.DisposeCount == 0);

    [Test]
    public void TheApplierDrawsWhatWasRecorded_NotWhatTheTreeBecameSince()
    {
        var first = AddControlAt(0);
        var second = AddControlAt(100);
        _cache.RecordComponents([first, second], Matrix4x4F.Identity);

        second.Arrange(new Rect(300, 0, 10, 10));
        var arrived = AddControlAt(500);

        Apply();

        Assert.Multiple(() =>
        {
            Assert.That(LiveUnitOf(first), Is.Not.Null);
            Assert.That(LiveUnitOf(second)?.LastTransform.TranslationVector.X, Is.EqualTo(100).Within(0.01),
                "drawn where it was recorded");
            Assert.That(LiveUnitOf(arrived), Is.Null, "not recorded, not drawn");
        });
    }

    [Test]
    public void OnlyANewRecordIsApplied()
    {
        var control = AddControlAt(0);

        Assert.That(Apply(), Is.False, "nothing recorded yet");

        _cache.RecordComponents([control], Matrix4x4F.Identity);
        Assert.That(Apply(), Is.True);
        Assert.That(Apply(), Is.False, "the record was applied once");
    }

    // The render thread can fall behind by a frame or two. A component a later record found clean records nothing and keeps
    // what it drew - which it drew only in the earlier record, so none of them may be skipped.
    [Test]
    public void SeveralRecordsBeforeAnApply_AllCount()
    {
        var first = AddControlAt(0);
        var second = AddControlAt(100);
        _cache.RecordComponents([first], Matrix4x4F.Identity);
        _cache.RecordComponents([first, second], Matrix4x4F.Identity);

        Apply();

        Assert.Multiple(() =>
        {
            Assert.That(LiveUnitOf(first), Is.Not.Null);
            Assert.That(LiveUnitOf(second), Is.Not.Null);
        });
    }

    // A pointer over a row swaps its fill: the same component, drawn again, in the place it already had.
    [Test]
    public void AComponentThatChangedHowItLooks_IsDrawnAnew()
    {
        var control = AddControlAt(0);
        _cache.RecordComponents([control], Matrix4x4F.Identity);
        Apply();

        control.Background = Brushes.Blue;
        _cache.RecordComponents([control], Matrix4x4F.Identity);
        Apply();

        var fill = (LiveUnitOf(control)?.Payload as Adamantium.UI.Rendering.Payloads.RectanglePayload)?.Brush as SolidColorBrush;
        Assert.That(fill?.Color, Is.EqualTo(Colors.Blue));
    }

    // The transforms a record composes are its own. Kept from one record to the next, every element a popup or an adorner
    // ever showed stayed in memory with all it referenced - a closed menu held the whole grid it was opened over.
    [Test]
    public void EachRecord_StartsAFrameOfItsOwn()
    {
        var context = new FrameCountingContext();
        var cache = new RenderCache(context, _factory);
        var control = AddControlAt(0);

        cache.RecordComponents([control], Matrix4x4F.Identity);
        cache.RecordComponents([], Matrix4x4F.Identity);

        Assert.That(context.Frames, Is.EqualTo(2));
    }

    // ...and so does each build that records and applies at once, as a snapshot of a live subtree does.
    [Test]
    public void EachFusedBuild_StartsAFrameOfItsOwn()
    {
        var context = new FrameCountingContext();
        var cache = new RenderCache(context, _factory);
        var control = AddControlAt(0);

        cache.BuildFromComponents([control], Matrix4x4F.Identity);
        cache.BuildFromComponents([], Matrix4x4F.Identity);

        Assert.That(context.Frames, Is.EqualTo(2));
    }

    private sealed class FrameCountingContext : DrawingContext, IDrawingContextInternal
    {
        public int Frames { get; private set; }

        void IDrawingContextInternal.BeginRecordFrame()
        {
            Frames++;
            BeginRecordFrame();
        }
    }

    [Test]
    public void WhatTheNextRecordLeavesOut_IsNoLongerDrawn()
    {
        var kept = AddControlAt(0);
        var dropped = AddControlAt(100);
        _cache.RecordComponents([kept, dropped], Matrix4x4F.Identity);
        Apply();

        _cache.RecordComponents([kept], Matrix4x4F.Identity);
        Apply();

        Assert.Multiple(() =>
        {
            Assert.That(LiveUnitOf(kept), Is.Not.Null);
            Assert.That(LiveUnitOf(dropped), Is.Null);
        });
    }
}
