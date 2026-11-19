using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A plane only looked at (<see cref="InfiniteCanvas.IsReadOnly"/>): panned, its items picked and copied, but
/// nothing changed by the user - no drawing tool, no grips, no drag, no delete, paste, group, order or undo, and the
/// inspector only shows. Turned off, everything comes back.</summary>
public class CanvasReadOnlyTests
{
    private static (DrawingBoard Canvas, CanvasScene Scene) Stage()
    {
        var canvas = new DrawingBoard();
        var window = new Window { Width = 800, Height = 600, Content = canvas };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        canvas.Measure(new Size<double>(800, 600), force: true);
        canvas.Arrange(new Rect(0, 0, 800, 600));
        var scene = new CanvasScene();
        canvas.Scene = scene;
        return (canvas, scene);
    }

    private static ShapeItem Box(double x, double y) =>
        new(CanvasShape.Rectangle, new Rect(x, y, 60, 40), Brushes.White, 2);

    private static CanvasPointerEventArgs At(double x, double y) => new()
    {
        World = new Vector2<double>(x, y),
        Pointer = new Vector2<double>(x, y),
        Screen = new Vector2<double>(x, y),
        Button = MouseButtons.Left,
        ClickCount = 1
    };

    private static void Press(InfiniteCanvas canvas, Key key, InputModifiers modifiers = InputModifiers.None) =>
        canvas.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        });

    [Test]
    public void TheToolInHand_IsPutDownForTheOneThatPicks_AndComesBackOff()
    {
        var (canvas, _) = Stage();
        var select = new SelectTool();
        var pen = new PenTool();
        canvas.Tools.Add(select);
        canvas.Tools.Add(pen);
        canvas.Tool = pen;

        canvas.IsReadOnly = true;
        var whileReadOnly = canvas.Tool;
        canvas.Tool = pen;
        var penAsked = canvas.Tool;
        canvas.IsReadOnly = false;
        canvas.Tool = pen;

        Assert.Multiple(() =>
        {
            Assert.That(whileReadOnly, Is.SameAs(select), "the pen stayed in hand");
            Assert.That(penAsked, Is.SameAs(select), "the pen was taken up again");
            Assert.That(canvas.Tool, Is.SameAs(pen), "the pen is not given back");
        });
    }

    [Test]
    public void WhatChangesThePlane_IsOff_WhatLooksAtIt_IsNot()
    {
        var (canvas, scene) = Stage();
        var first = Box(0, 0);
        var second = Box(200, 0);
        var third = Box(400, 0);
        scene.Add(first);
        scene.Add(second);
        scene.Add(third);
        canvas.BeginEdit("Move");
        first.Move(new Vector2<double>(5, 0));
        canvas.EndEdit();
        canvas.SelectMany([first, second, third], false);

        canvas.IsReadOnly = true;

        Assert.Multiple(() =>
        {
            Assert.That(canvas.UndoCommand.CanExecute(), Is.False);
            Assert.That(canvas.DeleteCommand.CanExecute(), Is.False);
            Assert.That(canvas.GroupCommand.CanExecute(), Is.False);
            Assert.That(canvas.UngroupCommand.CanExecute(), Is.False);
            Assert.That(canvas.BringToFrontCommand.CanExecute(), Is.False);
            Assert.That(canvas.SendToBackCommand.CanExecute(), Is.False);
            Assert.That(canvas.AlignCommand.CanExecute("Left"), Is.False);
            Assert.That(canvas.SpreadCommand.CanExecute("Across"), Is.False);
            Assert.That(canvas.ClearCommand.CanExecute(), Is.False);
            Assert.That(canvas.ImportSvgCommand.CanExecute(), Is.False);
            Assert.That(canvas.OfferedHandles, Is.EqualTo(CanvasHandles.None), "grips are offered");
            Assert.That(canvas.FitSelectionCommand.CanExecute(), Is.True, "looking at the selection is still allowed");
        });

        canvas.IsReadOnly = false;

        Assert.Multiple(() =>
        {
            Assert.That(canvas.UndoCommand.CanExecute(), Is.True);
            Assert.That(canvas.DeleteCommand.CanExecute(), Is.True);
            Assert.That(canvas.ClearCommand.CanExecute(), Is.True);
            Assert.That(canvas.OfferedHandles, Is.Not.EqualTo(CanvasHandles.None));
        });
    }

    [Test]
    public void Keys_CopyOnly()
    {
        var (canvas, scene) = Stage();
        var item = Box(0, 0);
        scene.Add(item);
        canvas.SelectMany([item], false);
        canvas.IsReadOnly = true;

        Press(canvas, Key.V, InputModifiers.LeftControl);
        Press(canvas, Key.D, InputModifiers.LeftControl);
        Press(canvas, Key.Delete);
        Press(canvas, Key.C, InputModifiers.LeftControl);
        var copied = canvas.CanPaste;
        var afterKeys = scene.Items.Count;
        canvas.IsReadOnly = false;
        canvas.SelectMany([item], false);
        Press(canvas, Key.V, InputModifiers.LeftControl);

        Assert.Multiple(() =>
        {
            Assert.That(afterKeys, Is.EqualTo(1), "a key put something on the plane or took it off");
            Assert.That(copied, Is.True, "copying is looking, and was refused");
            Assert.That(scene.Items, Has.Count.EqualTo(2), "what was copied is pasted once the plane may change");
        });
    }

    [Test]
    public void APressPicks_ButADragMovesNothing()
    {
        var (canvas, scene) = Stage();
        var item = Box(0, 0);
        scene.Add(item);
        var select = new SelectTool();
        canvas.Tools.Add(select);
        canvas.Tool = select;
        canvas.IsReadOnly = true;

        // On its outline: the box is not filled.
        select.OnPressed(canvas, At(0, 20));
        var pickedOnPress = canvas.IsSelected(item);
        select.OnMoved(canvas, At(100, 120));
        select.OnReleased(canvas, At(100, 120));
        var picked = pickedOnPress && canvas.IsSelected(item);
        var stayed = item.Bounds.X;
        canvas.IsReadOnly = false;
        select.OnPressed(canvas, At(0, 20));
        select.OnMoved(canvas, At(100, 120));
        select.OnReleased(canvas, At(100, 120));

        Assert.Multiple(() =>
        {
            Assert.That(picked, Is.True, "a press does not pick on a plane only looked at");
            Assert.That(stayed, Is.EqualTo(0).Within(1e-9), "the drag moved what was picked");
            Assert.That(item.Bounds.X, Is.EqualTo(100).Within(1e-9), "the same drag moves it once the plane may change");
        });
    }

    [Test]
    public void ADragUnderWay_EndsWhenThePlaneTurnsReadOnly()
    {
        var (canvas, scene) = Stage();
        var item = Box(0, 0);
        scene.Add(item);
        var select = new SelectTool();
        canvas.Tools.Add(select);
        canvas.Tool = select;

        select.OnPressed(canvas, At(0, 20));
        select.OnMoved(canvas, At(10, 20));
        canvas.IsReadOnly = true;
        select.OnMoved(canvas, At(100, 20));
        select.OnReleased(canvas, At(100, 20));

        Assert.That(item.Bounds.X, Is.EqualTo(10).Within(1e-9), "the drag went on over a plane only looked at");
    }

    [Test]
    public void ThePickingTool_GivenLater_IsTakenUp_AndAnInspectorMovedOn_IsGivenBack()
    {
        var (canvas, _) = Stage();
        var pen = new PenTool();
        canvas.Tool = pen;
        canvas.IsReadOnly = true;
        var withNoPicker = canvas.Tool;
        var select = new SelectTool();
        canvas.DefaultTool = select;
        var grid = new PropertyGrid();
        canvas.Inspector = grid;
        var lockedWhileHere = grid.IsReadOnly;
        canvas.Inspector = null;

        var ownLock = new PropertyGrid { IsReadOnly = true };
        canvas.Inspector = ownLock;
        canvas.IsReadOnly = false;

        Assert.Multiple(() =>
        {
            Assert.That(withNoPicker, Is.Null, "no tool picks, so none is kept");
            Assert.That(canvas.Tool, Is.SameAs(pen), "turned off, the pen asked for comes back");
            Assert.That(lockedWhileHere, Is.True);
            Assert.That(grid.IsReadOnly, Is.False, "an inspector moved on stays read-only");
            Assert.That(ownLock.IsReadOnly, Is.True, "the application's own read-only inspector was made editable");
        });
    }

    [Test]
    public void TheRail_OffersOnlyWhatPicks_AndTheInspector_OnlyShows()
    {
        var (canvas, _) = Stage();
        canvas.Tools.Add(new SelectTool());
        canvas.Tools.Add(new PenTool());
        canvas.Tools.Add(new ShapeTool(CanvasShape.Rectangle));
        var rail = new CanvasToolRail { Canvas = canvas };
        var grid = new PropertyGrid();
        canvas.Inspector = grid;
        var buttonsBefore = rail.Children.Count;

        canvas.IsReadOnly = true;
        var buttonsReadOnly = rail.Children.Count;
        var gridReadOnly = grid.IsReadOnly;
        canvas.IsReadOnly = false;

        Assert.Multiple(() =>
        {
            Assert.That(buttonsBefore, Is.EqualTo(3));
            Assert.That(buttonsReadOnly, Is.EqualTo(1), "drawing tools are offered on a plane only looked at");
            Assert.That(rail.Children.Count, Is.EqualTo(3));
            Assert.That(gridReadOnly, Is.True, "the inspector writes into a plane only looked at");
            Assert.That(grid.IsReadOnly, Is.False);
        });
    }
}
