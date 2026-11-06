using System.Collections.Generic;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Sandbox.Canvases.ViewModels;

/// <summary>Drawing board tab: ink, shapes, curves, text and pictures on an unbounded plane, with controls put down among
/// them.</summary>
[ViewModel]
public partial class DrawingBoardViewModel : CanvasPageViewModel
{
    public DrawingBoardViewModel() : base("DrawingBoard", "drawing-board-layout.json")
    {
        // The ORDER is the order the rail shows them in, and the only thing said here about showing them: what each one
        // is called, what it looks like and which key picks it are the tool's own business.
        Tools.Add(PenTool);
        Tools.Add(TextTool);
        Tools.Add(ErasePointTool);
        Tools.Add(EraseStrokeTool);
        Tools.Add(RectangleTool);
        Tools.Add(EllipseTool);
        Tools.Add(LineTool);
        Tools.Add(ArrowTool);
        Tools.Add(CurveTool);
        Tools.Add(PolygonTool);
        Tools.Add(ButtonTool);
        Tools.Add(CheckTool);
        Tools.Add(BoxTool);
        Tools.Add(TextureTool);

        // The drawing as data: the board draws shapes from descriptions and templates everything else.
        Put(-300, -90, 200, 48, new SampleButton("Press me"));
        Put(-300, 0, 200, 40, new SampleSwitch("Check me"));
        Put(-300, 70, 200, 40, new SampleField("A field"));

        // ON THE ORIGIN, so it stays in the middle at every zoom - the one place a control shows what a zoom does to it
        // without the camera being driven anywhere.
        Put(-100, -24, 200, 48, new SampleButton("Zoom me"));

        // A ROUNDED box with a fat outline: the case where the outline has to eat inwards instead of making the shape
        // bigger.
        Put(-420, 150, 220, 130, new CanvasShapeViewModel(CanvasShape.Rectangle)
        {
            Stroke = new SolidColorBrush(Colors.MediumSpringGreen),
            Thickness = 16,
            Fill = new SolidColorBrush(Color.FromRgba(40, 90, 70, 200)),
            Corner = new Adamantium.ProceduralGeometry.CornerRadius(28, 28, 0, 28)
        });

        Put(-160, 150, 140, 140, new CanvasShapeViewModel(CanvasShape.Polygon)
        {
            Stroke = new SolidColorBrush(Colors.Orange),
            Thickness = 6,
            Fill = new SolidColorBrush(Color.FromRgba(120, 70, 20, 200)),
            Sides = 6
        });

        // NO outline at all: the fill has to keep the whole box, since there is nothing to make room for.
        Put(100, 150, 140, 100, new CanvasShapeViewModel(CanvasShape.Rectangle)
        {
            Stroke = new SolidColorBrush(Colors.Orange),
            Thickness = 0,
            Fill = new SolidColorBrush(Colors.DeepPink)
        });

        // Two arrows, one of each head, so both can be seen against the same line thickness.
        Put(-420, 330, 200, 0, new CanvasShapeViewModel(CanvasShape.Arrow)
        {
            Stroke = new SolidColorBrush(Colors.White),
            Thickness = 3
        });

        // FAT barbs, which is where the corner at the tip either closes or does not.
        Put(-160, 330, 200, 60, new CanvasShapeViewModel(CanvasShape.Arrow)
        {
            Stroke = new SolidColorBrush(Colors.MediumSpringGreen),
            Thickness = 18,
            StartHead = CanvasArrowHead.Barbs,
            EndHead = CanvasArrowHead.Barbs
        });

        // A plain LINE beside them, because a line and a straight ink stroke look alike on screen and behave nothing
        // alike: the line is reshaped by its two ends and wears no frame, the stroke is ink and keeps its box.
        Put(100, 330, 220, 70, new CanvasShapeViewModel(CanvasShape.Line)
        {
            Stroke = new SolidColorBrush(Colors.DeepSkyBlue),
            Thickness = 6
        });

        Restore();
    }

    public ICanvasTool PenTool { get; } = new PenTool();

    public ICanvasTool RectangleTool { get; } = new ShapeTool(CanvasShape.Rectangle);

    public ICanvasTool EllipseTool { get; } = new ShapeTool(CanvasShape.Ellipse);

    public ICanvasTool LineTool { get; } = new ShapeTool(CanvasShape.Line);

    /// <summary>One tool for every regular polygon - how many sides is a setting of the tool, not a tool of its
    /// own.</summary>
    public ICanvasTool PolygonTool { get; } = new ShapeTool(CanvasShape.Polygon);

    /// <summary>An arrow. Which head each end wears is a setting, so an application wanting a double-headed one beside
    /// this builds a second of the same class.</summary>
    public ICanvasTool ArrowTool { get; } = new ShapeTool(CanvasShape.Arrow);

    /// <summary>A curve. ONE button for all three kinds - which one suits a line is decided by looking at it, so the
    /// kind is changed in the panel afterwards rather than chosen in advance.</summary>
    public ICanvasTool CurveTool { get; } = new CurveTool { Name = "Curve" };

    public ICanvasTool TextTool { get; } = new TextTool();

    /// <summary>Two erasers and not one with a switch: which of them you want is the same kind of choice as which tool
    /// you want, so it is made in the same place and in the same way.</summary>
    public ICanvasTool ErasePointTool { get; } = new EraseTool(CanvasEraseMode.Point);

    public ICanvasTool EraseStrokeTool { get; } = new EraseTool(CanvasEraseMode.Stroke);

    /// <summary>The toolbox: each entry is the same tool with a different factory, because what a control IS belongs to
    /// the application and not to the board. A NEW control every time it is used - one instance handed out twice would
    /// be one control that cannot be in two places.</summary>
    // NAMED and PICTURED here and nowhere else. A control tool has no look of its own - what control it puts down is
    // this application's choice - so the application is what says which of the theme's pictures fits.
    public ICanvasTool ButtonTool { get; } =
        new ElementTool(() => new Button { Content = "Press me" })
        { Name = "Button", Icon = "CanvasToolButtonIcon", Description = "drag out a button" };

    public ICanvasTool CheckTool { get; } =
        new ElementTool(() => new CheckBox { Content = "Check me" }, new Size<double>(130, 28))
        { Name = "Check box", Icon = "CanvasToolCheckBoxIcon", Description = "drag out a checkbox" };

    public ICanvasTool BoxTool { get; } =
        new ElementTool(() => new TextBox { Text = "Editable" }, new Size<double>(160, 30))
        { Name = "Text box", Icon = "CanvasToolTextBoxIcon", Description = "drag out a text box" };

    /// <summary>A TEXTURE - a surface to paint a picture on. The engine's own tool: what it puts down is a plain fill and
    /// the file is chosen afterwards in the panel, so there is nothing here to decide.</summary>
    public ICanvasTool TextureTool { get; } = new TextureTool();

    protected override void ToolPicked()
    {
        RaisePropertyChanged(nameof(PenSettings));
        RaisePropertyChanged(nameof(EraserSettings));
        RaisePropertyChanged(nameof(TextSettings));
        RaisePropertyChanged(nameof(ShapeSettings));
    }

    // Which settings the FIRST face shows - the ones belonging to the tool in hand. The same shape of answer the
    // selection sections give, so the two faces of the inspector are one panel changing its contents and not two
    // panels taking turns.
    public Visibility PenSettings => Tool is PenTool ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EraserSettings => Tool is EraseTool ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TextSettings => Tool is TextTool ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ShapeSettings => Tool is ShapeTool ? Visibility.Visible : Visibility.Collapsed;

    [Bindable] private Color _inkColor = Colors.White;

    /// <summary>What the pen paints with. A NEW brush on every change, never the same one repainted: a finished stroke
    /// keeps the brush it was drawn with, so repainting it would go back and recolor everything already on the plane.
    /// </summary>
    public Brush Ink { get; private set; } = new SolidColorBrush(Colors.White);

    partial void OnInkColorChanged(Color value)
    {
        Ink = new SolidColorBrush(value);
        RaisePropertyChanged(nameof(Ink));
    }

    [Bindable] private Color _fillColor = Colors.SteelBlue;

    public Brush ShapeFill { get; private set; } = new SolidColorBrush(Colors.SteelBlue);

    partial void OnFillColorChanged(Color value)
    {
        ShapeFill = new SolidColorBrush(value);
        RaisePropertyChanged(nameof(ShapeFill));
    }

    /// <summary>Whether a new shape is filled at all. Off, a rectangle is a frame - and a frame is what you can press
    /// through, which is the difference a hit test makes.</summary>
    [Bindable] private bool _filled = true;

    partial void OnFilledChanged(bool value) => RaisePropertyChanged(nameof(ShapeFillOrNothing));

    public Brush ShapeFillOrNothing => _filled ? ShapeFill : null;

    [Bindable] private double _inkThickness = 2;

    [Bindable] private double _textSize = 16;

    [Bindable] private double _eraserSize = 18;

    [Bindable] private bool _snapToGrid;

    /// <summary>Whether the plate with the coordinates rides with the pointer while the grid is pulling.</summary>
    [Bindable] private bool _showReadout = true;

    [Bindable] private Color _snapColor = Colors.DodgerBlue;

    public Brush SnapMark { get; private set; } = new SolidColorBrush(Colors.DodgerBlue);

    partial void OnSnapColorChanged(Color value)
    {
        SnapMark = new SolidColorBrush(value);
        RaisePropertyChanged(nameof(SnapMark));
    }

    [Bindable] private double _snapMarkSize = 9;

    public IReadOnlyList<CanvasArrowHead> ArrowHeads { get; } =
        [CanvasArrowHead.None, CanvasArrowHead.Barbs, CanvasArrowHead.Triangle];

    /// <summary>THE DRAWING: this page's own things on the plane - where each is and what it is. The board draws a shape
    /// from its description and builds a control for anything else; nothing here is a scene item and nothing here is a
    /// control.</summary>
    public TrackingCollection<ICanvasObject> Objects { get; } = new();

    // One thing on the plane: where it goes and what it is.
    private void Put(double left, double top, double width, double height, object what) =>
        Objects.Add(new CanvasObjectViewModel
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            Content = what
        });

    protected override void SelectionMoved()
    {
        RaisePropertyChanged(nameof(StrokeSection));
        RaisePropertyChanged(nameof(ShapeSection));
        RaisePropertyChanged(nameof(TextSection));
        RaisePropertyChanged(nameof(GroupSection));
        RaisePropertyChanged(nameof(CurveSection));
        RaisePropertyChanged(nameof(IsNurbs));
        RaisePropertyChanged(nameof(IsBezier));
        RaisePropertyChanged(nameof(HasCorners));
        RaisePropertyChanged(nameof(HasSides));
        RaisePropertyChanged(nameof(HasHeads));
    }

    // A section per KIND, shown when the selection holds one. Several kinds at once show several sections, which is
    // what a mixed selection honestly is.
    public Visibility StrokeSection => Shown<StrokeItem>();

    public Visibility ShapeSection => Shown<ShapeItem>();

    public Visibility TextSection => Shown<TextItem>();

    public Visibility GroupSection => Shown<GroupItem>();

    public Visibility CurveSection => Shown<CurveItem>();

    protected override bool Describable =>
        base.Describable || StrokeSection == Visibility.Visible || ShapeSection == Visibility.Visible ||
        TextSection == Visibility.Visible || CurveSection == Visibility.Visible || GroupSection == Visibility.Visible;

    public IReadOnlyList<CanvasCurve> Curves { get; } =
        [CanvasCurve.Bezier, CanvasCurve.BSpline, CanvasCurve.Nurbs];

    /// <summary>Evenness belongs to a NURBS and to nothing else - the other two curves would show a line that does
    /// nothing.</summary>
    public bool IsNurbs => AnyCurve(CanvasCurve.Nurbs);

    /// <summary>Whether the order is worth showing: a Bezier has one that follows from its points, and the other two
    /// kinds say what they are some other way.</summary>
    public bool IsBezier => AnyCurve(CanvasCurve.Bezier);

    private bool AnyCurve(CanvasCurve kind)
    {
        if (Selection == null) return false;

        foreach (var item in Selection)
        {
            if (item is CurveItem curve && curve.Kind == kind) return true;
        }

        return false;
    }

    // Within the one Shape section, the lines that belong to ONE shape. Rounding is a rectangle's and sides are a
    // polygon's; an ellipse has neither. A line that means nothing to what is selected is worse than no line, because
    // it invites a number that will quietly do nothing.
    public bool HasCorners => HasShape(CanvasShape.Rectangle);

    public bool HasSides => HasShape(CanvasShape.Polygon);

    public bool HasHeads => HasShape(CanvasShape.Arrow);

    private bool HasShape(CanvasShape shape)
    {
        if (Selection == null) return false;

        foreach (var item in Selection)
        {
            if (item is ShapeItem found && found.Shape == shape) return true;
        }

        return false;
    }

    // The CONTEXT BAR's actions. Actions and not properties: what a thing looks like is the inspector's business and
    // saying it twice would be two places to keep in step - what belongs beside the selection is what you do to it.
    [Command] private void BringToFront() => Reorder(front: true);

    [Command] private void SendToBack() => Reorder(front: false);

    // Back to front when raising and front to back when lowering, so a group keeps its own order instead of being
    // reversed by each item leapfrogging the last.
    private void Reorder(bool front)
    {
        if (Selection is not { Count: > 0 } chosen) return;

        for (var i = 0; i < chosen.Count; i++)
        {
            var item = front ? chosen[i] : chosen[chosen.Count - 1 - i];

            if (front) Scene.BringToFront(item);
            else Scene.SendToBack(item);
        }
    }

    /// <summary>The color of what is selected, for the bar beside it: the first one's, and writing it paints them all.
    /// <para>Every kind of item keeps its color under its own name - a stroke and a shape do not agree - so the answer
    /// is given here rather than asked of a common property none of them has.</para></summary>
    public Color SelectionColor
    {
        get => Selection is { Count: > 0 } chosen && ColorOf(chosen[0]) is { } color ? color : _inkColor;
        set
        {
            if (Selection is not { Count: > 0 } chosen) return;

            var brush = new SolidColorBrush(value);
            foreach (var item in chosen) Paint(item, brush);

            Scene.Touch();
            RaisePropertyChanged(nameof(SelectionColor));
        }
    }

    private static Color? ColorOf(ICanvasItem item) => item switch
    {
        StrokeItem stroke => (stroke.Brush as SolidColorBrush)?.Color,
        ShapeItem shape => (shape.Stroke as SolidColorBrush)?.Color,
        TextItem text => (text.Brush as SolidColorBrush)?.Color,
        _ => null
    };

    private static void Paint(ICanvasItem item, Brush brush)
    {
        switch (item)
        {
            case StrokeItem stroke:
                stroke.Brush = brush;
                break;

            case ShapeItem shape:
                shape.Stroke = brush;
                break;

            case TextItem text:
                text.Brush = brush;
                break;
        }
    }
}
