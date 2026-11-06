using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Sandbox.Localization;
using Adamantium.UI.Sandbox.ViewModels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Sandbox.Canvases.ViewModels;

/// <summary>What the drawing board's tab and the node graph's tab have in common: the plane. The wheel zooms toward the
/// cursor, the middle button - or space and the left one - pans, and neither ever reaches an edge.
/// <para>The camera's <c>Scale</c> and <c>Offset</c> are bound both ways, so the readout is the state itself rather than
/// a copy of it.</para></summary>
[ViewModel]
public abstract partial class CanvasPageViewModel : TabPageViewModel
{
    private readonly string _layoutFile;

    protected CanvasPageViewModel(string name, string layoutFile) : base(name)
    {
        _layoutFile = layoutFile;

        // The page counts what it holds - the canvas never tells it, because the canvas does not hold it.
        Scene.Changed += OnSceneChanged;

        // FIRST on every rail: the one tool that does not put anything down where the first press happens to land.
        Tools.Add(SelectTool);
    }

    /// <summary>Opens on SELECT, unless the last run left something else in hand - called by the page once its tools
    /// are all on the rail, so that what is read back finds the tool it names.</summary>
    protected void Restore()
    {
        Tool = SelectTool;
        Layout = Read();
    }

    /// <summary>HOW THE CANVAS WAS LEFT - panels, camera, tool - as the canvas itself writes it down. The application
    /// decides where that text lives; this one keeps it beside the user's own settings.</summary>
    [Bindable] private string _layout;

    private string Kept => System.IO.Path.Combine(ApplicationData.Folder, _layoutFile);

    private string Read()
    {
        try
        {
            return System.IO.File.Exists(Kept) ? System.IO.File.ReadAllText(Kept) : null;
        }
        catch (System.IO.IOException)
        {
            return null;
        }
    }

    partial void OnLayoutChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        // A setting nobody asked for must never be what stops the application: a profile on a drive that has gone
        // away is a panel in the wrong place, and nothing worse than that.
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Kept));
            System.IO.File.WriteAllText(Kept, value);
        }
        catch (System.IO.IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void OnSceneChanged(object sender, EventArgs e)
    {
        RefreshStructure();
    }

    /// <summary>Which face of the inspector is showing: the list of what is on the plane, or the properties. The two
    /// are one choice, so the toggles that drive them only answer to being turned ON - a radio never unchecks itself,
    /// and pressing the face that is already showing leaves it showing.</summary>
    [Bindable] private bool _showsStructure;

    public bool ShowsProperties
    {
        get => !ShowsStructure;
        set { if (value) ShowsStructure = false; }
    }

    partial void OnShowsStructureChanged(bool value)
    {
        RaisePropertyChanged(nameof(ShowsProperties));
        RaisePropertyChanged(nameof(ToolFace));
        RaisePropertyChanged(nameof(SelectionFace));
        RaisePropertyChanged(nameof(StructureFace));
        RaisePropertyChanged(nameof(Face));
    }

    /// <summary>What is on the plane, topmost first like a layer list; a fresh array each time so bound lists see the
    /// change.</summary>
    public IReadOnlyList<ICanvasItem> Structure { get; private set; } = Array.Empty<ICanvasItem>();

    private void RefreshStructure()
    {
        var items = Scene.Items;
        var listed = new List<ICanvasItem>(items.Count);

        for (var i = items.Count - 1; i >= 0; i--) listed.Add(items[i]);

        Structure = listed;
        RaisePropertyChanged(nameof(Structure));

        // The row that stands for what is selected follows the selection rather than the other way round here: the
        // list was rebuilt, and a stale row would point at an object that has left the plane.
        RaisePropertyChanged(nameof(Listed));
    }

    /// <summary>The row the list has picked out. Writing it selects on the plane; reading it follows what the canvas
    /// selected, however that happened - clicking the plane and clicking the list are one selection, not two.</summary>
    public ICanvasItem Listed
    {
        get => _selection is { Count: 1 } one ? one[0] : null;
        set
        {
            if (value == null || (_selection is { Count: 1 } already && ReferenceEquals(already[0], value))) return;

            Selection = [value];
        }
    }

    public IReadOnlyList<CanvasGridStyle> GridStyles { get; } =
        [CanvasGridStyle.Dots, CanvasGridStyle.Lines, CanvasGridStyle.None, CanvasGridStyle.Transparent];

    /// <summary>What was done on this plane. The PAGE's, like the scene - undo belongs to whoever owns the work, and the
    /// canvas only says where one step ends and the next begins.</summary>
    public CanvasHistory History { get; } = new();

    /// <summary>What is on the plane. The PAGE holds it, not the canvas - undo, saving and everything else the work is
    /// for belong to whoever owns it, which is why the control only ever asks what is visible.</summary>
    public CanvasScene Scene { get; } = new();

    /// <summary>The tools, made once and kept. A tool holds the gesture it is halfway through, so a fresh one on every
    /// click of a tool button would be a tool that forgets what it was doing.</summary>
    // The wire refuses in RED while it is being pulled, so "this socket will not take it" is answered before the button
    // is let go rather than by the wire quietly not appearing.
    public ICanvasTool SelectTool { get; } =
        new SelectTool { Wires = { RefusedStroke = new SolidColorBrush(Colors.OrangeRed) } };

    /// <summary>The tools the canvas offers, in the order its rail shows them. THE list - there is no second copy of
    /// it in the markup, because everything a button needs (the name, the picture, the key) is a fact about the tool.
    /// </summary>
    public CanvasTools Tools { get; } = new();

    [Bindable] private ICanvasTool _tool;

    partial void OnToolChanged(ICanvasTool value)
    {
        RaisePropertyChanged(nameof(Face));
        ToolPicked();
    }

    /// <summary>The tool in hand changed - a page with settings for its own tools says so here.</summary>
    protected virtual void ToolPicked() { }

    /// <summary>A caption over the rows, and NULL whenever it would only repeat them. The sections already name what
    /// they hold, so a caption saying the same word above them is noise; several objects at once is the one case the
    /// sections cannot state, because then there are several of them.</summary>
    public string Face =>
        ShowsProperties && _selection is { Count: > 1 } many ? $"{many.Count} objects selected" : null;

    /// <summary>Whether a press on a control on the plane MOVES it or presses it.</summary>
    [Bindable] private bool _designMode = true;

    /// <summary>How far in the camera may go. A practical limit and not a technical one - see the canvas's own note on
    /// MaxScale - so it belongs in the panel where the rest of the surface is set.</summary>
    [Bindable] private double _maxZoom = 65536;

    /// <summary>Whether ONE object is worth a question before it goes. A switch, because with undo in place being asked
    /// every time is noise - and which of the two the work wants is not knowable in advance. Clearing the WHOLE scene
    /// always asks: there is no gesture that does it by accident, so being asked is never a surprise, and there is a lot
    /// on the other side of the answer.</summary>
    [Bindable] private bool _asksBeforeDelete = true;

    [Command]
    private async Task Erase()
    {
        if (Scene.Items.Count == 0) return;

        // Resolved here rather than taken in the constructor: this page is built with `new` by the gallery, so there is
        // nothing to inject it through, and threading a service through the gallery to reach one dialog is worse than
        // asking the application that owns both.
        var dialogs = UIApplication.Current?.Container?.Resolve<IDialogService>();

        if (dialogs != null)
        {
            var result = await dialogs.ShowDialogAsync<ConfirmDialogViewModel>(new NavigationParameters()
                .Add("title", CanvasPageStrings.ClearTitle)
                .Add("message", CanvasPageStrings.ClearQuestion(Scene.Items.Count)));

            if (result.Result != DialogButtonResult.Ok) return;
        }

        // Through the HISTORY: clearing the plane is the one action most worth being able to take back.
        History.Record(Scene, "Clear", Scene.Clear);
        Selection = Array.Empty<ICanvasItem>();
    }

    [Bindable] private CanvasGridStyle _gridStyle = CanvasGridStyle.Dots;

    [Bindable] private double _scale = 1;

    [Bindable] private Vector2<double> _offset;

    /// <summary>The zoom for the view bar as a multiplier ("0.4x"), matching the other camera readouts.</summary>
    public string ZoomText => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.###}x", _scale);

    partial void OnScaleChanged(double value) => RaisePropertyChanged(nameof(ZoomText));

    /// <summary>What the canvas has selected, taken straight off it. The inspector reads THIS and not a copy: the
    /// canvas publishes a fresh snapshot on every change, which is what lets a binding notice.</summary>
    [Bindable] private IReadOnlyList<ICanvasItem> _selection;

    partial void OnSelectionChanged(IReadOnlyList<ICanvasItem> value)
    {
        RaisePropertyChanged(nameof(ToolFace));
        RaisePropertyChanged(nameof(SelectionFace));
        RaisePropertyChanged(nameof(ElementSection));
        RaisePropertyChanged(nameof(HasPlainLabel));
        RaisePropertyChanged(nameof(Listed));
        RaisePropertyChanged(nameof(Face));
        SelectionMoved();
    }

    /// <summary>The selection changed - a page with sections of its own says so here.</summary>
    protected virtual void SelectionMoved() { }

    // One inspector panel with several faces: properties or structure by the user's choice; properties show the tool
    // with nothing selected, the selection otherwise.
    public Visibility ToolFace => ShowsProperties && !Anything ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SelectionFace =>
        ShowsProperties && Anything && Describable ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StructureFace => ShowsStructure ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ElementSection => Shown<ElementItem>();

    /// <summary>Whether the generic "what the control says" line belongs in the panel at all. A node says it in its own
    /// Title line, and the same name written on two lines is a panel that contradicts itself the moment one of them is
    /// edited - which it did, showing the old name above the new one.</summary>
    public bool HasPlainLabel
    {
        get
        {
            if (_selection == null) return false;

            foreach (var item in _selection)
            {
                if (item is ElementItem and not ElementItem { Element: CanvasNode }) return true;
            }

            return false;
        }
    }

    // LINING UP and SPREADING OUT, one command each and the canvas handed in as the parameter. One command taking a word
    // would need a second parameter to say WHICH canvas, and a command takes one.
    [Command]
    private void AlignLeft(object which) => Do(which, c => c.Align(CanvasAlignment.Left));

    [Command]
    private void AlignMiddle(object which) => Do(which, c => c.Align(CanvasAlignment.HorizontalCenter));

    [Command]
    private void AlignRight(object which) => Do(which, c => c.Align(CanvasAlignment.Right));

    [Command]
    private void AlignTop(object which) => Do(which, c => c.Align(CanvasAlignment.Top));

    [Command]
    private void SpreadColumns(object which) => Do(which, c => c.Spread(CanvasSpread.Horizontal));

    [Command]
    private void SpreadRows(object which) => Do(which, c => c.Spread(CanvasSpread.Vertical));

    /// <summary>Brings the work into view: what is selected, or the whole plane when nothing is.</summary>
    [Command]
    private void FitView(object which) => Do(which, c => { if (!c.FitSelection()) c.FitAll(); });

    private static void Do(object which, Action<InfiniteCanvas> what)
    {
        if (which is InfiniteCanvas canvas) what(canvas);
    }

    protected bool Anything => _selection is { Count: > 0 };

    /// <summary>Whether the panel has a section for what is selected. A WIRE has none and wants none: it is the two
    /// sockets it joins and nothing else, so there is nothing to set. Without this the panel stood there as an empty
    /// frame with a border round no rows, which reads as a panel that has lost its contents.</summary>
    protected virtual bool Describable => Shown<ElementItem>() == Visibility.Visible;

    protected Visibility Shown<T>() where T : ICanvasItem
    {
        if (_selection == null) return Visibility.Collapsed;

        foreach (var item in _selection)
        {
            if (item is T) return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    // The same question one level down: not what KIND of item is selected, but what kind of control one of them is
    // carrying. Every control on the plane is the same item, so its own lines can only be found this way.
    protected Visibility ShownElement<T>()
    {
        if (_selection == null) return Visibility.Collapsed;

        foreach (var item in _selection)
        {
            if (item is ElementItem { Element: T }) return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }
}
