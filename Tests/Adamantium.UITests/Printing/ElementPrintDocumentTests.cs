using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Printing;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>An element printed as it shows: each element drawn by its own rendering where it lies in the element, laid on
/// the sheet inside what the printer reaches, cut, faded and hidden as on the screen, and run on over the sheets it needs.
/// </summary>
public class ElementPrintDocumentTests
{
    // A sheet of 400 by 300 the printer reaches 360 by 240 of, from (20, 30).
    private static readonly PrintPage Sheet = new(new PaperSize("Test", 400, 300), PageOrientation.Portrait,
        new Thickness(20, 30, 20, 30));

    private static Border Card(double width, double height, out Border inner)
    {
        inner = new Border
        {
            Width = 50,
            Height = 20,
            Margin = new Thickness(30, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Colors.Blue),
        };
        var card = new Border { Width = width, Height = height, Background = new SolidColorBrush(Colors.Red), Child = inner };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(card);
        return card;
    }

    private static Vector2<double> OriginOf(Matrix4x4F transform)
    {
        var at = Vector3F.TransformCoordinate(new Vector3F(0, 0, 0), transform);
        return new Vector2<double>(at.X, at.Y);
    }

    private static Vector2<double> Scaled(Matrix4x4F transform)
    {
        var at = Vector3F.TransformCoordinate(new Vector3F(1, 0, 0), transform);
        return new Vector2<double>(at.X - OriginOf(transform).X, 0);
    }

    [Test]
    public void EachElement_IsDrawnWhereItLiesInTheElement_InsideWhatThePrinterReaches()
    {
        var card = Card(200, 100, out _);
        var document = new ElementPrintDocument(card, "Card");
        var session = new RecordingDrawingSession();

        Assert.That(document.Paginate(Sheet), Is.EqualTo(1));
        document.DrawPage(0, session);

        var drawn = session.Geometries.Select(entry => OriginOf(entry.Transform)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(drawn, Has.Length.EqualTo(2), "the card and the box in it");
            Assert.That(drawn[0], Is.EqualTo(new Vector2<double>(20, 30)), "the card at the corner the printer reaches");
            Assert.That(drawn[1], Is.EqualTo(new Vector2<double>(50, 40)), "the box at its margin inside the card");
            Assert.That(session.GroupsOpen, Is.Zero);
        });
    }

    [Test]
    public void ATallElement_RunsOnDownTheSheets()
    {
        var card = Card(200, 500, out _);
        var document = new ElementPrintDocument(card);
        var session = new RecordingDrawingSession();

        Assert.That(document.Paginate(Sheet), Is.EqualTo(3), "500 over 240 a sheet");
        document.DrawPage(1, session);

        Assert.That(OriginOf(session.Geometries[0].Transform), Is.EqualTo(new Vector2<double>(20, 30 - 240)),
            "the second sheet starts 240 down the card");
    }

    [Test]
    public void ASheet_LeavesOutWhatLiesWhollyOnOtherSheets()
    {
        var card = Card(200, 500, out var inner);
        inner.Margin = new Thickness(30, 400, 0, 0);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(card);
        var document = new ElementPrintDocument(card);
        var first = new RecordingDrawingSession();
        var second = new RecordingDrawingSession();

        document.Paginate(Sheet);
        document.DrawPage(0, first);
        document.DrawPage(1, second);

        Assert.Multiple(() =>
        {
            Assert.That(first.Geometries, Has.Count.EqualTo(1), "the box 400 down is not on the first sheet");
            Assert.That(second.Geometries, Has.Count.EqualTo(2), "the card runs on over the second, the box with it");
        });
    }

    [Test]
    public void AWideElement_IsShrunkToTheWidth_OrAtItsOwnSizeRunsOnAcross()
    {
        var card = Card(720, 100, out _);
        var fitted = new ElementPrintDocument(card);
        var own = new ElementPrintDocument(card, fit: ElementFit.None);
        var session = new RecordingDrawingSession();

        Assert.Multiple(() =>
        {
            Assert.That(fitted.Paginate(Sheet), Is.EqualTo(1));
            Assert.That(own.Paginate(Sheet), Is.EqualTo(2), "720 over 360 a sheet, across");
        });

        fitted.DrawPage(0, session);
        own.DrawPage(1, session);
        Assert.Multiple(() =>
        {
            Assert.That(Scaled(session.Geometries[0].Transform).X, Is.EqualTo(0.5).Within(1e-6), "half its size");
            Assert.That(OriginOf(session.Geometries[2].Transform), Is.EqualTo(new Vector2<double>(20 - 360, 30)),
                "the second sheet across starts 360 into the card");
        });
    }

    [Test]
    public void AnElementCutToItsBoundsAndFaded_IsCutAndFadedOnTheSheet_AndAHiddenOneIsLeftOut()
    {
        var card = Card(200, 100, out var inner);
        card.ClipToBounds = true;
        card.Opacity = 0.5;
        inner.Visibility = Visibility.Hidden;
        var document = new ElementPrintDocument(card);
        var session = new RecordingDrawingSession();

        document.Paginate(Sheet);
        document.DrawPage(0, session);

        Assert.Multiple(() =>
        {
            Assert.That(session.Groups.Count(group => group.Opacity == 0.5), Is.EqualTo(1), "faded once, as a whole");
            Assert.That(session.Groups.Count(group => group.Clip is RectangleGeometry), Is.EqualTo(2),
                "cut to what the printer reaches, and to the card");
            Assert.That(session.Geometries, Has.Count.EqualTo(1), "the hidden box draws nothing");
            Assert.That(session.GroupsOpen, Is.Zero);
        });
    }

    [Test]
    public void Fades_MultiplyDownToEachElementsOwnDrawing_AndALayerIsFadedWhole()
    {
        var card = Card(200, 100, out var inner);
        card.Opacity = 0.5;
        inner.Opacity = 0.5;
        var session = new RecordingDrawingSession();
        var document = new ElementPrintDocument(card);
        document.Paginate(Sheet);
        document.DrawPage(0, session);

        var layered = Card(200, 100, out var held);
        layered.Opacity = 0.5;
        layered.IsLayer = true;
        held.Opacity = 0.5;
        var whole = new RecordingDrawingSession();
        document = new ElementPrintDocument(layered);
        document.Paginate(Sheet);
        document.DrawPage(0, whole);

        Assert.Multiple(() =>
        {
            Assert.That(session.Groups.Select(group => group.Opacity).Where(opacity => opacity < 1),
                Is.EqualTo(new[] { 0.5, 0.25 }), "the card's own drawing, then the box's - faded by both");
            Assert.That(whole.Groups.Select(group => group.Opacity).Where(opacity => opacity < 1),
                Is.EqualTo(new[] { 0.5, 0.5 }), "the layer as a whole, then the box in it by its own fade alone");
        });
    }

    [Test]
    public void ALayer_IsFadedByTheFadesAboveIt_TooAsOnTheScreen()
    {
        var card = Card(200, 100, out var inner);
        card.Opacity = 0.5;
        inner.IsLayer = true;
        inner.Opacity = 0.5;
        var session = new RecordingDrawingSession();
        var document = new ElementPrintDocument(card);

        document.Paginate(Sheet);
        document.DrawPage(0, session);

        Assert.That(session.Groups.Select(group => group.Opacity).Where(opacity => opacity < 1),
            Is.EqualTo(new[] { 0.5, 0.25 }), "the card's own drawing, then the box laid down by both fades");
    }

    [Test]
    public void WhatARoundedStrokedElementHolds_IsCutInsideItsStroke()
    {
        var card = Card(200, 100, out _);
        card.ClipToBounds = true;
        card.CornerRadius = new Adamantium.ProceduralGeometry.CornerRadius(14);
        card.BorderThickness = new Thickness(3);
        card.BorderBrush = new SolidColorBrush(Colors.Black);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(card);
        var session = new RecordingDrawingSession();
        var document = new ElementPrintDocument(card);

        document.Paginate(Sheet);
        document.DrawPage(0, session);

        var clips = session.Groups.Select(group => group.Clip).OfType<RectangleGeometry>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(clips, Has.Length.EqualTo(3), "the sheet, the card's edge, inside its stroke");
            Assert.That(clips[1].Rect, Is.EqualTo(new Rect(0, 0, 200, 100)));
            Assert.That(clips[1].CornerRadius.TopLeft, Is.EqualTo(14));
            Assert.That(clips[2].Rect, Is.EqualTo(new Rect(3, 3, 194, 94)));
            Assert.That(clips[2].CornerRadius.TopLeft, Is.EqualTo(11));
        });
    }

    [Test]
    public void APage_IsDrawnOnlyOnceTheElementIsLaidOnASheet_AndOnlyOneItMade()
    {
        var document = new ElementPrintDocument(Card(200, 100, out _));

        Assert.Throws<System.InvalidOperationException>(() => document.DrawPage(0, new RecordingDrawingSession()));
        document.Paginate(Sheet);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => document.DrawPage(1, new RecordingDrawingSession()));
    }

    [Test]
    public void Printing_LeavesTheElementsRenderingAsItWas()
    {
        var card = Card(200, 100, out var inner);
        var before = (card.IsGeometryValid, inner.IsGeometryValid);
        var document = new ElementPrintDocument(card);

        document.Paginate(Sheet);
        document.DrawPage(0, new RecordingDrawingSession());

        Assert.That((card.IsGeometryValid, inner.IsGeometryValid), Is.EqualTo(before));
    }
}
