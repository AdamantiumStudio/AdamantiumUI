using System.Collections;
using System.IO;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>A drawing on an <see cref="InfiniteCanvas"/>: ink, shapes, curves, text and pictures, and the controls an
/// application puts among them. It reads and writes SVG - a drawing is the kind of work other people's tools have a claim
/// on. A graph of nodes is a <see cref="NodeGraph"/>.</summary>
public class DrawingBoard : InfiniteCanvas
{
    private readonly CanvasDrawingHost _drawing = new();
    private CanvasCommand _toSvg;
    private CanvasCommand _fromSvg;

    private static readonly IReadOnlyList<CanvasArrowHead> Heads =
        [CanvasArrowHead.None, CanvasArrowHead.Barbs, CanvasArrowHead.Triangle];

    private static readonly IReadOnlyList<CanvasCurve> Curving =
        [CanvasCurve.Bezier, CanvasCurve.BSpline, CanvasCurve.Nurbs];

    // A PICTURE's own catalogs. Every one of them is a brush's property: what is offered is the whole of what the
    // engine can paint a picture with, so a drawing is not a poorer place to use a texture than a window is.
    private static readonly IReadOnlyList<Stretch> Filling =
        [Stretch.Fill, Stretch.Uniform, Stretch.UniformToFill, Stretch.None];

    private static readonly IReadOnlyList<TileMode> Tiling =
        [TileMode.None, TileMode.Tile, TileMode.FlipX, TileMode.FlipY, TileMode.FlipXY];

    private static readonly IReadOnlyList<ImageBackgroundState> Grounding =
        [ImageBackgroundState.WhenEmpty, ImageBackgroundState.Always, ImageBackgroundState.Never];

    /// <summary>The application's drawing objects, as <see cref="NodeGraph.Nodes"/> is for a graph; shapes become
    /// canvas drawings, anything else a control from <see cref="ObjectTemplateSelector"/>.</summary>
    public static readonly AdamantiumProperty ObjectsProperty = AdamantiumProperty.Register(nameof(Objects),
        typeof(IEnumerable), typeof(DrawingBoard), new PropertyMetadata(null, OnObjectsChanged));

    public IEnumerable Objects
    {
        get => GetValue<IEnumerable>(ObjectsProperty);
        set => SetValue(ObjectsProperty, value);
    }

    private static void OnObjectsChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is DrawingBoard board) board._drawing.SetObjects(e.NewValue as IEnumerable);
    }

    /// <summary>What draws the objects that are not shapes - picked for the type of what each one carries, the same way
    /// a node's body is.</summary>
    public static readonly AdamantiumProperty ObjectTemplateSelectorProperty = AdamantiumProperty.Register(
        nameof(ObjectTemplateSelector), typeof(DataTemplateSelector), typeof(DrawingBoard),
        new PropertyMetadata(null, OnObjectTemplateSelectorChanged));

    public DataTemplateSelector ObjectTemplateSelector
    {
        get => GetValue<DataTemplateSelector>(ObjectTemplateSelectorProperty);
        set => SetValue(ObjectTemplateSelectorProperty, value);
    }

    private static void OnObjectTemplateSelectorChanged(AdamantiumComponent component,
        AdamantiumPropertyChangedEventArgs e)
    {
        if (component is DrawingBoard board) board._drawing.SetTemplateSelector(e.NewValue as DataTemplateSelector);
    }

    /// <summary>What the pen leaves behind. Null means a press draws nothing, which is what a drawing being used to LOOK
    /// at something wants.</summary>
    public static readonly AdamantiumProperty InkProperty = AdamantiumProperty.Register(nameof(Ink),
        typeof(Brush), typeof(DrawingBoard), new PropertyMetadata(null));

    /// <summary>The pen width in screen pixels, converted to world units when a stroke starts, so the stroke then scales
    /// with the zoom.</summary>
    public static readonly AdamantiumProperty InkThicknessProperty = AdamantiumProperty.Register(nameof(InkThickness),
        typeof(Double), typeof(DrawingBoard), new PropertyMetadata(2.0));

    /// <summary>How big new text is, in SCREEN pixels - the same division the pen makes: the size is what you see while
    /// you type, and the words it leaves belong to the plane and grow with it.</summary>
    public static readonly AdamantiumProperty TextSizeProperty = AdamantiumProperty.Register(nameof(TextSize),
        typeof(Double), typeof(DrawingBoard), new PropertyMetadata(16.0));

    /// <summary>How wide the eraser is, in SCREEN pixels - the ring the hand aims with, so it stays the same size
    /// however far the camera is zoomed. What it takes out of the drawing is that ring turned into world units, which
    /// is why a zoomed-in eraser rubs a finer hole.</summary>
    public static readonly AdamantiumProperty EraserSizeProperty = AdamantiumProperty.Register(nameof(EraserSize),
        typeof(Double), typeof(DrawingBoard), new PropertyMetadata(18.0, PropertyMetadataOptions.AffectsRender));

    /// <summary>What a newly drawn shape is filled with, or nothing for an outline only.</summary>
    public static readonly AdamantiumProperty ShapeFillProperty = AdamantiumProperty.Register(nameof(ShapeFill),
        typeof(Brush), typeof(DrawingBoard), new PropertyMetadata(null));

    public Brush Ink
    {
        get => GetValue<Brush>(InkProperty);
        set => SetValue(InkProperty, value);
    }

    public Double InkThickness
    {
        get => GetValue<Double>(InkThicknessProperty);
        set => SetValue(InkThicknessProperty, value);
    }

    public Double TextSize
    {
        get => GetValue<Double>(TextSizeProperty);
        set => SetValue(TextSizeProperty, value);
    }

    public Double EraserSize
    {
        get => GetValue<Double>(EraserSizeProperty);
        set => SetValue(EraserSizeProperty, value);
    }

    public Brush ShapeFill
    {
        get => GetValue<Brush>(ShapeFillProperty);
        set => SetValue(ShapeFillProperty, value);
    }

    /// <summary>The ends an arrow can wear.</summary>
    public static readonly AdamantiumProperty ArrowHeadsProperty = AdamantiumProperty.Register(nameof(ArrowHeads),
        typeof(IEnumerable), typeof(DrawingBoard), new PropertyMetadata(Heads));

    public IEnumerable ArrowHeads => GetValue<IEnumerable>(ArrowHeadsProperty);

    /// <summary>The kinds of curve a drawn line can be made into.</summary>
    public static readonly AdamantiumProperty CurvesProperty = AdamantiumProperty.Register(nameof(Curves),
        typeof(IEnumerable), typeof(DrawingBoard), new PropertyMetadata(Curving));

    public IEnumerable Curves => GetValue<IEnumerable>(CurvesProperty);

    /// <summary>The ways a picture can fill its tile.</summary>
    public static readonly AdamantiumProperty FillsProperty = AdamantiumProperty.Register(nameof(Fills),
        typeof(IEnumerable), typeof(DrawingBoard), new PropertyMetadata(Filling));

    public IEnumerable Fills => GetValue<IEnumerable>(FillsProperty);

    /// <summary>...and the ways that tile can repeat, mirrored or not.</summary>
    public static readonly AdamantiumProperty TilingsProperty = AdamantiumProperty.Register(nameof(Tilings),
        typeof(IEnumerable), typeof(DrawingBoard), new PropertyMetadata(Tiling));

    public IEnumerable Tilings => GetValue<IEnumerable>(TilingsProperty);

    /// <summary>When the ground behind a picture is painted.</summary>
    public static readonly AdamantiumProperty GroundsProperty = AdamantiumProperty.Register(nameof(Grounds),
        typeof(IEnumerable), typeof(DrawingBoard), new PropertyMetadata(Grounding));

    public IEnumerable Grounds => GetValue<IEnumerable>(GroundsProperty);

    /// <summary>Writes the drawing out as SVG - what is SELECTED when something is, and everything when nothing is,
    /// which is the same rule the fit button follows.
    /// <para>SVG because a drawing is the kind of work other people's tools have a claim on. A graph means something
    /// only here and is kept as JSON; see <see cref="CanvasSvg"/>.</para></summary>
    public CanvasCommand ExportSvgCommand => _toSvg ??= new CanvasCommand(_ => ExportSvg(), _ => Drawn());

    /// <summary>...and reads one in, adding what it holds to the plane rather than replacing it: a drawing opened on
    /// top of nothing is the same thing as opening it, and one opened onto work already there is a person bringing a
    /// piece in from elsewhere.</summary>
    public CanvasCommand ImportSvgCommand => _fromSvg ??= new CanvasCommand(_ => ImportSvg(), _ => Scene != null);

    /// <summary>Raised when a drawing was read but something in it could not be - an arc, a gradient, a foreign
    /// element. The board says nothing on its own: what to tell the user, and how, is the application's.</summary>
    public event EventHandler<CanvasSvgReadEventArgs> SvgRead;

    /// <summary>What the dialog that writes the drawing out asks. The theme says it, and the three below, in the
    /// application's language.</summary>
    public static readonly AdamantiumProperty ExportTitleProperty = AdamantiumProperty.Register(nameof(ExportTitle),
        typeof(String), typeof(DrawingBoard), new PropertyMetadata(null));

    /// <summary>What the dialog that reads a drawing in asks.</summary>
    public static readonly AdamantiumProperty OpenTitleProperty = AdamantiumProperty.Register(nameof(OpenTitle),
        typeof(String), typeof(DrawingBoard), new PropertyMetadata(null));

    /// <summary>What both dialogs call the drawings they offer.</summary>
    public static readonly AdamantiumProperty DrawingsNameProperty = AdamantiumProperty.Register(nameof(DrawingsName),
        typeof(String), typeof(DrawingBoard), new PropertyMetadata(null));

    /// <summary>...and what they call every other file.</summary>
    public static readonly AdamantiumProperty AllFilesNameProperty = AdamantiumProperty.Register(nameof(AllFilesName),
        typeof(String), typeof(DrawingBoard), new PropertyMetadata(null));

    public String ExportTitle
    {
        get => GetValue<String>(ExportTitleProperty);
        set => SetValue(ExportTitleProperty, value);
    }

    public String OpenTitle
    {
        get => GetValue<String>(OpenTitleProperty);
        set => SetValue(OpenTitleProperty, value);
    }

    public String DrawingsName
    {
        get => GetValue<String>(DrawingsNameProperty);
        set => SetValue(DrawingsNameProperty, value);
    }

    public String AllFilesName
    {
        get => GetValue<String>(AllFilesNameProperty);
        set => SetValue(AllFilesNameProperty, value);
    }

    private bool Drawn()
    {
        if (Scene == null) return false;

        foreach (var item in ItemsHere())
        {
            if (CanvasSvg.Drawable(item)) return true;
        }

        return false;
    }

    private void ExportSvg()
    {
        if (!FileDialog.IsAvailable) return;

        var chosen = FileDialog.Save(new SaveFileRequest
        {
            Title = ExportTitle,
            DefaultExtension = "svg",
            FileTypes = SvgFiles(),

            // ITS OWN MEMORY, and its own window. The first means this dialog comes back the size it was left and in
            // the folder a drawing was last written to - not wherever some other dialog of this application was. The
            // second means it opens on the screen the board is on: a dialog belongs to a window, and one belonging to
            // nothing opens on the main screen, which on two monitors is the wrong one half the time.
            Key = "canvas.svg.export",
            Owner = GetWindow()?.Handle ?? IntPtr.Zero
        });

        if (chosen == null) return;   // canceled is an answer

        var wanted = new List<ICanvasItem>();

        // WHAT IS SELECTED, or everything: a person who has picked three shapes out of a drawing and pressed export
        // meant those three.
        foreach (var item in Selection.Count > 0 ? Selection : (IEnumerable<ICanvasItem>)ItemsHere())
        {
            if (CanvasSvg.Drawable(item)) wanted.Add(item);
        }

        try
        {
            File.WriteAllText(chosen, CanvasSvg.Save(wanted));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A file that cannot be written - a full disk, a folder somebody else owns - is a fact about the world.
            SvgRead?.Invoke(this, new CanvasSvgReadEventArgs(0, 0, e.Message));
        }
    }

    private void ImportSvg()
    {
        if (Scene == null || !FileDialog.IsAvailable) return;

        var chosen = FileDialog.Open(new OpenFileRequest
        {
            Title = OpenTitle,
            FileTypes = SvgFiles(),
            Key = "canvas.svg.import",
            Owner = GetWindow()?.Handle ?? IntPtr.Zero
        });

        if (chosen == null) return;

        string text;
        try
        {
            text = File.ReadAllText(chosen);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            SvgRead?.Invoke(this, new CanvasSvgReadEventArgs(0, 0, e.Message));
            return;
        }

        var made = CanvasSvg.Load(text, out var skipped);

        // ONE STEP in the board's memory, however many shapes arrived: opening a drawing is one thing a person did, and
        // taking it back should not mean pressing undo four hundred times.
        BeginEdit("Open a drawing");
        try
        {
            foreach (var item in made) Scene.Add(item);

            SelectMany(made, false);
        }
        finally
        {
            EndEdit();
        }

        SvgRead?.Invoke(this, new CanvasSvgReadEventArgs(made.Count, skipped, null));
    }

    private IReadOnlyList<FileType> SvgFiles() =>
    [
        new(DrawingsName, "svg"),
        new(AllFilesName, "*")
    ];

    /// <summary>The objects of a scene shown here are made, and the application's objects followed both ways.</summary>
    protected override void OnSceneShown(ICanvasScene scene) => _drawing.SetScene(scene);

    protected override void OnSceneLeft() => _drawing.StopFollowing();

    protected override void OnCommandsAsk()
    {
        _toSvg?.RaiseCanExecuteChanged();
        _fromSvg?.RaiseCanExecuteChanged();
    }

    protected override bool PenErases => true;
}
