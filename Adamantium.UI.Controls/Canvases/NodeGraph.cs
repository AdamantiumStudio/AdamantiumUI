using System.Collections;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>A graph of nodes on an <see cref="InfiniteCanvas"/>: the application's node objects, each shown as a
/// <see cref="CanvasNode"/>, the wires between their sockets, the list of kinds a dropped wire opens, and the titled frames
/// that comment on a group of them. Nothing is drawn by hand here - that is a <see cref="DrawingBoard"/>.</summary>
public class NodeGraph : InfiniteCanvas
{
    private readonly CanvasGraphHost _graph;
    private CanvasPane _palette;
    private CanvasCommand _comment;

    private ElementItem _askedFrom;
    private CanvasNodePin _askedPin;
    private Vector2<double> _askedWorld;

    public NodeGraph()
    {
        _graph = new CanvasGraphHost(this);
    }

    /// <summary>The GRAPH: the application's own node objects, one container made for each. Written from both sides -
    /// a node made on the plane appears here, one removed from here leaves the plane - so this collection is the only
    /// account of what the graph is, and the canvas keeps no second copy of it.
    /// <para>Nothing about it is a control: see <see cref="ICanvasNode"/> for the whole of what a node has to say.
    /// </para></summary>
    public static readonly AdamantiumProperty NodesProperty = AdamantiumProperty.Register(nameof(Nodes),
        typeof(IEnumerable), typeof(NodeGraph), new PropertyMetadata(null, OnNodesChanged));

    public IEnumerable Nodes
    {
        get => GetValue<IEnumerable>(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    private static void OnNodesChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is NodeGraph graph) graph._graph.SetNodes(e.NewValue as IEnumerable);
    }

    /// <summary>The application's node kinds (<see cref="ICanvasNodeKind"/>): the one list the palette, inspector and loader
    /// all read, and how the graph makes nodes.</summary>
    public static readonly AdamantiumProperty NodeKindsProperty = AdamantiumProperty.Register(nameof(NodeKinds),
        typeof(IEnumerable), typeof(NodeGraph), new PropertyMetadata(null, OnNodeKindsChanged));

    public IEnumerable NodeKinds
    {
        get => GetValue<IEnumerable>(NodeKindsProperty);
        set => SetValue(NodeKindsProperty, value);
    }

    /// <summary>The catalog of node kinds changed - what the palette rebuilds its sections on.</summary>
    public event EventHandler NodeKindsChanged;

    private static void OnNodeKindsChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not NodeGraph graph) return;

        graph._graph.SetKinds(e.NewValue as IEnumerable);
        graph.NodeKindsChanged?.Invoke(graph, EventArgs.Empty);
    }

    /// <summary>The kinds a SOCKET may carry, said the same way. Two sockets join when their kinds agree, so a name
    /// typed by hand is a wire that quietly will not go on.</summary>
    public static readonly AdamantiumProperty SocketKindsProperty = AdamantiumProperty.Register(nameof(SocketKinds),
        typeof(IEnumerable), typeof(NodeGraph), new PropertyMetadata(null, OnSocketKindsChanged));

    public IEnumerable SocketKinds
    {
        get => GetValue<IEnumerable>(SocketKindsProperty);
        set => SetValue(SocketKindsProperty, value);
    }

    /// <summary>The catalog of socket kinds changed - what the line that names what a socket carries offers.</summary>
    public event EventHandler SocketKindsChanged;

    private static void OnSocketKindsChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not NodeGraph graph) return;

        graph._graph.SetSocketKinds(e.NewValue as IEnumerable);
        graph.SocketKindsChanged?.Invoke(graph, EventArgs.Empty);
    }

    /// <summary>Puts a node of that kind on the plane, at that point in the world - by adding it to
    /// <see cref="Nodes"/>, which is the only way anything gets onto the graph.
    /// <para>Returns what was added, or null when there is no collection to add to: a graph showing nodes it was never
    /// given cannot grow one.</para></summary>
    public ICanvasNode AddNode(String kind, Vector2<double> at) => _graph.Add(kind, at);

    /// <summary>Whether the list of node kinds is offered at all.</summary>
    public static readonly AdamantiumProperty ShowsNodePaletteProperty = AdamantiumProperty.Register(
        nameof(ShowsNodePalette), typeof(Boolean), typeof(NodeGraph), new PropertyMetadata(true));

    public Boolean ShowsNodePalette
    {
        get => GetValue<Boolean>(ShowsNodePaletteProperty);
        set => SetValue(ShowsNodePaletteProperty, value);
    }

    /// <summary>Raised when a wire is let go over empty plane. See <see cref="CanvasWireDroppedEventArgs"/> for why the
    /// graph offers this instead of deciding it.</summary>
    public event EventHandler<CanvasWireDroppedEventArgs> WireDropped;

    /// <summary>Offers a dropped wire to whoever is listening. Called by the gesture; true when somebody took it.
    /// </summary>
    internal bool OfferWire(ElementItem item, CanvasNodePin pin, Vector2<double> world)
    {
        if (WireDropped != null)
        {
            var args = new CanvasWireDroppedEventArgs
            {
                FromItem = item,
                FromPin = pin,
                World = world,
                Screen = WorldToScreen(world)
            };

            WireDropped(this, args);
            if (args.Handled) return true;
        }

        // Nobody took it, so the graph offers what it can: the list of kinds it was given. Without one the wire is
        // simply abandoned, which is what letting go over nothing usually means.
        return OpenPalette(item, pin, world);
    }

    /// <summary>Whether the list of node kinds is showing. Two-way: the application's own list closes itself by writing
    /// false here.</summary>
    public static readonly AdamantiumProperty IsPaletteOpenProperty = AdamantiumProperty.Register(
        nameof(IsPaletteOpen), typeof(Boolean), typeof(NodeGraph),
        new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault, OnPaletteOpenChanged));

    /// <summary>The same state as something markup can bind straight to a panel: a question in code and a visibility in
    /// markup are not the same shape, and keeping them in step here is a line rather than a converter in three themes.
    /// </summary>
    public static readonly AdamantiumProperty PaletteFaceProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(PaletteFace), typeof(Visibility), typeof(NodeGraph),
        new PropertyMetadata(Visibility.Collapsed));

    /// <summary>Where the list opens, in SCREEN pixels from the graph's corner - under the pointer, which is where the
    /// hand already is.</summary>
    public static readonly AdamantiumProperty PaletteAtProperty = AdamantiumProperty.Register(nameof(PaletteAt),
        typeof(Vector2<double>), typeof(NodeGraph), new PropertyMetadata(Vector2<double>.Zero));

    /// <summary>What was picked out of the list - an entry of <see cref="NodeKinds"/>, not a word. PICKING IS THE
    /// ANSWER: a list opened by a gesture is answered by choosing from it, so writing here puts that node on the plane
    /// and closes the list.</summary>
    public static readonly AdamantiumProperty PickedKindProperty = AdamantiumProperty.Register(nameof(PickedKind),
        typeof(ICanvasNodeKind), typeof(NodeGraph),
        new PropertyMetadata(null, PropertyMetadataOptions.BindsTwoWayByDefault, OnPickedKindChanged));

    public Boolean IsPaletteOpen
    {
        get => GetValue<Boolean>(IsPaletteOpenProperty);
        set => SetValue(IsPaletteOpenProperty, value);
    }

    public Visibility PaletteFace => GetValue<Visibility>(PaletteFaceProperty);

    public Vector2<double> PaletteAt
    {
        get => GetValue<Vector2<double>>(PaletteAtProperty);
        set => SetValue(PaletteAtProperty, value);
    }

    public ICanvasNodeKind PickedKind
    {
        get => GetValue<ICanvasNodeKind>(PickedKindProperty);
        set => SetValue(PickedKindProperty, value);
    }

    private static void OnPaletteOpenChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not NodeGraph graph) return;

        var open = e.NewValue is true;
        graph.SetValue(PaletteFaceProperty, open ? Visibility.Visible : Visibility.Collapsed);

        if (open) return;

        graph._askedFrom = null;
        graph._askedPin = null;
    }

    /// <summary>Opens the list of kinds at that point in the world, for whoever wants a node there and has no wire to
    /// join - the tool that places one, or a double click on empty plane.</summary>
    public Boolean AskForNode(Vector2<double> world) => OpenPalette(null, null, world);

    private bool OpenPalette(ElementItem item, CanvasNodePin pin, Vector2<double> world)
    {
        if (NodeKinds == null || Nodes == null) return false;

        _askedFrom = item;
        _askedPin = pin;
        _askedWorld = world;

        PaletteAt = WorldToScreen(world);
        _palette?.PlaceAt(PaletteAt);
        IsPaletteOpen = true;

        return true;
    }

    // A KIND was picked: the node goes where the list was opened, and the wire that opened it lands on it.
    private static void OnPickedKindChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not NodeGraph graph || e.NewValue is not ICanvasNodeKind picked) return;

        var kind = picked.Kind;

        var from = graph._askedFrom;
        var pin = graph._askedPin;

        graph.IsPaletteOpen = false;

        graph.BeginEdit("Add node");
        try
        {
            var made = graph.AddNode(kind, graph._askedWorld);
            if (made == null) return;

            // The wire arrives at the FIRST socket of the other side - the one an editor picks when a wire is dropped
            // on a node rather than on one of its sockets, because it is the only one it can pick without asking.
            if (pin != null && from != null && graph.ItemOf(made) is { } item)
            {
                var node = item.Element as CanvasNode;
                var wanted = pin.IsInput ? node?.OutputPins : node?.InputPins;

                if (wanted is { Count: > 0 }) graph.Join(from, pin, item, wanted[0]);
            }
        }
        finally
        {
            graph.EndEdit();
            graph.SetCurrentValue(PickedKindProperty, null);
        }
    }

    /// <summary>The thing on the plane standing for that node, or null while it has not been made yet.</summary>
    public ElementItem ItemOf(ICanvasNode node) => _graph.ItemOf(node);

    /// <summary>WHAT DRAWS what a node carries, chosen for the type of the thing it is carrying. Given to the graph
    /// because the graph is what makes the containers: it hands each new node its template at birth.</summary>
    public static readonly AdamantiumProperty NodeContentSelectorProperty = AdamantiumProperty.Register(
        nameof(NodeContentSelector), typeof(DataTemplateSelector), typeof(NodeGraph),
        new PropertyMetadata(null, OnNodeContentSelectorChanged));

    public DataTemplateSelector NodeContentSelector
    {
        get => GetValue<DataTemplateSelector>(NodeContentSelectorProperty);
        set => SetValue(NodeContentSelectorProperty, value);
    }

    private static void OnNodeContentSelectorChanged(AdamantiumComponent component,
        AdamantiumPropertyChangedEventArgs e)
    {
        if (component is NodeGraph graph) graph._graph.SetContentSelector(e.NewValue as DataTemplateSelector);
    }

    /// <summary>Joins two nodes, the way the gesture does - for whoever answered <see cref="WireDropped"/> and has just
    /// made the node the wire was reaching for. Here and not in the application: the rules about what may be joined to
    /// what belong to the graph, not to whoever is adding to it.</summary>
    public ConnectionItem Join(ElementItem fromItem, CanvasNodePin from, ElementItem toItem, CanvasNodePin to)
    {
        if (Scene is not { } scene || !ConnectGesture.Joinable(scene, fromItem, from, toItem, to)) return null;

        ConnectionItem.MakeRoom(scene, from.IsInput ? from : to);

        var wire = from.IsInput
            ? new ConnectionItem(toItem, to, fromItem, from)
            : new ConnectionItem(fromItem, from, toItem, to);

        scene.Add(wire);
        from.IsConnected = true;
        to.IsConnected = true;

        return wire;
    }

    /// <summary>How much room a new frame leaves round what it is drawn about, in WORLD units - and the strip at the
    /// top is that much again, so the title has somewhere to sit that is not over a node.</summary>
    public static readonly AdamantiumProperty FrameMarginProperty = AdamantiumProperty.Register(nameof(FrameMargin),
        typeof(Double), typeof(NodeGraph), new PropertyMetadata(20.0));

    public Double FrameMargin
    {
        get => GetValue<Double>(FrameMarginProperty);
        set => SetValue(FrameMarginProperty, value);
    }

    /// <summary>Draws a titled frame round what is selected - the parameter is the title, "Comment" when there is
    /// none.</summary>
    public CanvasCommand FrameCommand => _comment ??= new CanvasCommand(
        title => FrameSelection(title as String),
        _ => Selection.Count > 0 && Scene != null);

    /// <summary>Draws a titled frame round what is selected and selects IT - the comment every graph editor has; with no
    /// title given, the theme's word for a comment. Null when nothing is selected. The frame holds nothing: what is
    /// inside it goes on being moved, wired and deleted without ever consulting it - see <see cref="CanvasFrameItem"/>.
    /// </summary>
    public CanvasFrameItem FrameSelection(string title = null)
    {
        if (Scene is not { } scene || SelectionBounds is not { } bounds) return null;

        var room = Math.Max(0, FrameMargin);
        var strip = room * 1.4;

        // A COPY of the accent, not the accent itself: the frame's color is a thing a person changes in the panel, and
        // written into the theme's own brush that change would repaint every accent in the application.
        var frame = new CanvasFrameItem(
            new Rect(bounds.X - room, bounds.Y - room - strip, bounds.Width + room * 2, bounds.Height + room * 2 + strip),
            title ?? Say("Comment"),
            SelectionBrush?.Copy()) { TitleHeight = strip };

        BeginEdit("Frame");
        try
        {
            // UNDER what it is about, and the scene draws in order - so it goes to the BACK. This is the whole of what
            // keeps it there: a frame left where it was made would be a sheet of color over the very nodes it is
            // drawn round.
            scene.Add(frame);
            scene.SendToBack(frame);

            Select(frame, false);
            scene.Touch();
        }
        finally
        {
            EndEdit();
        }

        return frame;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _palette = GetTemplateChild("PART_NodePalette") as CanvasPane;
    }

    /// <summary>The nodes of a scene shown here are made, and the application's node objects followed both ways.</summary>
    protected override void OnSceneShown(ICanvasScene scene) => _graph.SetScene(scene);

    protected override void OnSceneLeft() => _graph.StopFollowing();

    protected override void OnCommandsAsk() => _comment?.RaiseCanExecuteChanged();

    protected override bool Snaps => false;

    protected override bool OnEmptyPlaneDoubleClick(Vector2<double> world) => OfferWire(null, null, world);
}
