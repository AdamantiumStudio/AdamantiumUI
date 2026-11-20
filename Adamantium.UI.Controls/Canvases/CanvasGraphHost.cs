using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Converters;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>Keeps a canvas's nodes and the application's collection in step both ways, one container per model; wires live on
/// their sockets.</summary>
internal sealed class CanvasGraphHost
{
    private readonly Dictionary<ICanvasNode, ElementItem> _placed = new();
    private readonly Dictionary<CanvasNode, ICanvasNode> _modelOfNode = new();
    private readonly Dictionary<object, ICanvasNode> _modelOfSockets = new();
    private readonly Dictionary<ElementItem, CanvasNode> _built = new();
    private readonly Dictionary<CanvasConnection, ConnectionItem> _wired = new();
    private readonly Dictionary<ConnectionItem, CanvasConnection> _wireOf = new();
    private readonly HashSet<ICanvasSocket> _watched = new();
    private readonly NodeGraph _canvas;

    private IEnumerable _nodes;
    private IEnumerable _kinds;
    private IEnumerable _socketKinds;
    private SocketKindToBrushConverter _paint;
    private ICanvasScene _scene;
    private bool _reconciling;

    private Adamantium.UI.Core.Templates.DataTemplateSelector _selector;

    /// <summary>What draws the contents of every node here. Handed to each container as it is made, and to the ones
    /// already standing when it arrives - a selector set after the graph was filled must not leave those blank.
    /// </summary>
    public void SetContentSelector(Adamantium.UI.Core.Templates.DataTemplateSelector selector)
    {
        if (ReferenceEquals(_selector, selector)) return;

        _selector = selector;

        foreach (var (_, item) in _placed)
        {
            if (item.Element is CanvasNode node) node.ContentTemplateSelector = selector;
        }
    }

    /// <summary>The kinds a node here can be. Data, not a factory handed to the control: the shell is the engine's own
    /// and is made below, and what a kind MEANS is asked of the catalog entry.</summary>
    public void SetKinds(IEnumerable kinds) => _kinds = kinds;

    /// <summary>What a SOCKET may carry, and what each of those looks like. A pin takes its color from what flows
    /// through it, so every socket carrying the same thing looks the same across the whole graph.</summary>
    public void SetSocketKinds(IEnumerable kinds)
    {
        if (ReferenceEquals(_socketKinds, kinds)) return;

        _socketKinds = kinds;

        // WHAT PAINTS A PIN is made from that table and kept out of sight: the application says which word means which
        // color, and how a pin ends up wearing it is nobody else's business. Put INTO the binding that paints, which is
        // the only way a converter is ever used - nothing here calls it.
        _paint = kinds == null ? null : new SocketKindToBrushConverter(kinds);

        Repaint();
    }

    // The pins already standing were painted by what was there before - or by nothing at all, which is what an
    // application arriving after the graph finds.
    private void Repaint()
    {
        foreach (var (model, item) in _placed)
        {
            if (item.Element is CanvasNode node) Sockets(model, node);
        }
    }

    /// <summary>A node of that kind, at that place, INTO the collection - which is the only way anything joins the
    /// graph. Null when there is no collection to add to.</summary>
    public ICanvasNode Add(string kind, Vector2<double> at)
    {
        if (_nodes is not IList<ICanvasNode> list) return null;

        // ASKED OF THE CATALOG. A node is the application's object - the canvas has none of its own to reach for, the
        // same way an items control has no item type - so a canvas told nothing about kinds cannot invent one, and says
        // so by making nothing.
        if (KindOf(kind)?.Make() is not { } made) return null;

        // WHERE it goes is the only thing here that is the canvas's: this is the press that put it there.
        made.Left = at.X;
        made.Top = at.Y;

        list.Add(made);

        return made;
    }

    /// <summary>The catalog entry for a kind, or null when the catalog says nothing about it - a node of a kind
    /// nobody offers is still a node, it just has nothing inside.</summary>
    public ICanvasNodeKind KindOf(string kind)
    {
        if (_kinds == null) return null;

        foreach (var item in _kinds)
        {
            if (item is ICanvasNodeKind entry && entry.Kind == kind) return entry;
        }

        return null;
    }

    public CanvasGraphHost(NodeGraph canvas) => _canvas = canvas;

    /// <summary>The application's collection of nodes. Followed while it is set, so a node added to it appears on the
    /// plane and one taken out of it leaves.</summary>
    public void SetNodes(IEnumerable nodes)
    {
        if (ReferenceEquals(_nodes, nodes)) return;

        if (_nodes is INotifyCollectionChanged was) was.CollectionChanged -= OnNodesChanged;

        Clear();
        _nodes = nodes;

        if (_nodes is INotifyCollectionChanged now) now.CollectionChanged += OnNodesChanged;

        Fill();
    }

    /// <summary>The scene the containers are put in. Also where the answer comes BACK from: a drag moves the item, and
    /// items carry no notifications of their own, so the scene saying "something changed" is what there is.</summary>
    public void SetScene(ICanvasScene scene)
    {
        if (ReferenceEquals(_scene, scene)) return;

        TakeEvicted();
        if (_scene is CanvasScene was) was.Changed -= OnSceneChanged;

        _scene = scene;

        if (_scene is CanvasScene now) now.Changed += OnSceneChanged;

        // Heard again after a stop, which let go of the collection but not of which one it is.
        if (_scene != null && _nodes is INotifyCollectionChanged nodes)
        {
            nodes.CollectionChanged -= OnNodesChanged;
            nodes.CollectionChanged += OnNodesChanged;
        }

        Fill();
    }

    private void OnNodesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            Clear();
            Fill();
            return;
        }

        // A node MOVED in the application's list is still in the graph, wires and all: taking it out and putting it back
        // would cut every one of them.
        if (e.Action == NotifyCollectionChangedAction.Move) return;

        if (e.OldItems != null) Drop(e.OldItems);

        if (e.NewItems is not { Count: > 0 } added) return;

        if (added.Count == 1)
        {
            if (added[0] is ICanvasNode model) Put(model);
            return;
        }

        PutMany(added);
    }

    private void Fill()
    {
        if (_nodes != null) PutMany(_nodes);
    }

    // Many nodes in ONE change of the scene: the nodes, then their wires over them.
    private void PutMany(IEnumerable models)
    {
        if (_scene == null) return;

        var added = new List<ICanvasItem>();
        var placed = new List<ICanvasNode>();
        var standing = false;
        foreach (var entry in models)
        {
            if (entry is not ICanvasNode model || Place(model) is not { } item) continue;

            placed.Add(model);

            // Put back by an undo, an item may stand on the plane already: not a second time.
            if (_scene.Holds(item)) standing = true;
            else added.Add(item);
        }

        if (placed.Count == 0) return;

        // The wires AFTER the nodes, always: a wire is the two nodes it joins, and one whose ends are not on the plane
        // yet has nothing to be made of. Lines standing with an item put back are heard of as anything drawn is.
        if (standing) OnSceneChanged(this, EventArgs.Empty);

        foreach (var model in placed)
        {
            LinesOf(model.Inputs, added);
            LinesOf(model.Outputs, added);
        }

        using (new OwnChange(this)) _scene.ResetWithout(null, added);

        var ends = new HashSet<ICanvasNode>(placed);
        foreach (var model in placed)
        {
            Ends(model.Inputs, ends);
            Ends(model.Outputs, ends);
        }

        foreach (var end in ends) TakenOf(end);

        // Put in over the lines drawn before them, the nodes go under those once the edits of the moment are done.
        KeepWiresOnTop();
    }

    // EVERY WIRE OF THE GRAPH, found by walking the nodes' output sockets - each wire is reached once, from the end it
    // leaves - drawn anew over everything, in one change of the scene.
    private void RebuildWires()
    {
        if (_reconciling || _scene == null) return;

        // HELD while the scene is being changed, because the scene answers back: taking the old lines off it looks
        // exactly like somebody cutting them, and the graph would be told its own bookkeeping was an edit.
        var old = new HashSet<ICanvasItem>(_wireOf.Keys);
        var was = new Dictionary<CanvasConnection, ConnectionItem>(_wired);
        _wired.Clear();
        _wireOf.Clear();

        var lines = new List<ICanvasItem>();
        WireAll(lines);
        LookAsBefore(lines, was);

        using (new OwnChange(this)) _scene.ResetWithout(old, lines);

        TakenAll();
    }

    // The line of every wire of the nodes placed, into the list.
    private void WireAll(List<ICanvasItem> into)
    {
        foreach (var (model, _) in _placed)
        {
            foreach (var socket in model.Outputs)
            {
                foreach (var wire in socket.Connections)
                {
                    if (Wire(wire) is { } line) into.Add(line);
                }
            }
        }
    }

    private void TakenAll()
    {
        foreach (var (model, item) in _placed)
        {
            Taken(item, model.Inputs, true);
            Taken(item, model.Outputs, false);
        }
    }

    // The lines of one node's wires whose other end is on the plane too - a node just put on it.
    private void PutWiresOf(ICanvasNode model)
    {
        PutWiresOf(model.Inputs);
        PutWiresOf(model.Outputs);
        TakenAround(model);
    }

    private void PutWiresOf(Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> sockets)
    {
        foreach (var socket in sockets)
        {
            foreach (var wire in socket.Connections) PutWire(wire);
        }
    }

    // Whether its sockets, and those of every node a wire of it reaches, are taken - each node once, however many wires
    // join it.
    private void TakenAround(ICanvasNode model)
    {
        var ends = new HashSet<ICanvasNode> { model };
        Ends(model.Inputs, ends);
        Ends(model.Outputs, ends);

        foreach (var end in ends) TakenOf(end);
    }

    private static void Ends(Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> sockets,
        HashSet<ICanvasNode> into)
    {
        foreach (var socket in sockets)
        {
            foreach (var wire in socket.Connections)
            {
                if (wire.FromNode != null) into.Add(wire.FromNode);
                if (wire.ToNode != null) into.Add(wire.ToNode);
            }
        }
    }

    private void TakenOf(ICanvasNode model)
    {
        if (model == null || !_placed.TryGetValue(model, out var item)) return;

        Taken(item, model.Inputs, true);
        Taken(item, model.Outputs, false);
    }

    // WHETHER A SOCKET IS TAKEN is not a flag for anyone to set - it is whether a wire sits on it, and the socket is
    // the only account of that. Said here because this is where the wires are counted anyway: a socket filled in by
    // hand somewhere else is one that stays filled after the wire is cut.
    private static void Taken(ElementItem item, Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> sockets,
        bool input)
    {
        if (item.Element is not CanvasNode node) return;

        var pins = input ? node.InputPins : node.OutputPins;

        for (var i = 0; i < pins.Count && i < sockets.Count; i++)
        {
            pins[i].IsConnected = sockets[i].Connections.Count > 0;
        }
    }

    private void PutWire(CanvasConnection wire)
    {
        if (_scene == null || Wire(wire) is not { } item) return;

        using (new OwnChange(this)) _scene.Add(item);
    }

    // The line for a wire both of whose ends are on the plane, kept with it - or null for one already drawn, or not
    // drawable yet.
    private ConnectionItem Wire(CanvasConnection wire)
    {
        if (wire == null || _wired.ContainsKey(wire)) return null;

        if (wire.FromNode == null || wire.ToNode == null) return null;
        if (!_placed.TryGetValue(wire.FromNode, out var from) || !_placed.TryGetValue(wire.ToNode, out var to)) return null;
        if (Pin(from, wire.FromNode, wire.From, false) is not { } fromPin) return null;
        if (Pin(to, wire.ToNode, wire.To, true) is not { } toPin) return null;

        var item = new ConnectionItem(from, fromPin, to, toPin);

        _wired[wire] = item;
        _wireOf[item] = wire;
        return item;
    }

    private void TakeWire(CanvasConnection wire)
    {
        if (wire == null || !_wired.Remove(wire, out var item)) return;

        _wireOf.Remove(item);
        using (new OwnChange(this)) _scene?.Remove(item);
    }

    // The container's pin standing for the model's socket, found by WHERE the socket sits in the model's own list: the
    // two lists are made together and kept in step, so the n-th socket is the n-th pin.
    private static CanvasNodePin Pin(ElementItem item, ICanvasNode model, ICanvasSocket socket, bool input)
    {
        if (item.Element is not CanvasNode node || socket == null) return null;

        var sockets = input ? model.Inputs : model.Outputs;
        var pins = input ? node.InputPins : node.OutputPins;

        var at = sockets.IndexOf(socket);

        return at >= 0 && at < pins.Count ? pins[at] : null;
    }

    /// <summary>For a canvas that has been destroyed, or whose scene another canvas has taken: stops listening to the
    /// application's nodes, sockets, wires and scene, discards every node control it built, takes its items off the
    /// scene, which the application keeps along with the items, and forgets the scene until it is given one again. The
    /// model is not touched: the scene is let go of first, so nothing reads the removal as a cut.</summary>
    public void StopFollowing()
    {
        TakeEvicted();
        if (_nodes is INotifyCollectionChanged was)
        {
            was.CollectionChanged -= OnNodesChanged;
        }

        if (_scene is CanvasScene scene)
        {
            scene.Changed -= OnSceneChanged;
        }

        foreach (var (item, node) in _built)
        {
            Discard(item, node);
        }

        _built.Clear();
        Clear();
        _scene = null;
    }

    private void Clear()
    {
        var gone = new HashSet<ICanvasItem>();
        foreach (var (model, item) in _placed)
        {
            if (item.Element is CanvasNode going)
            {
                going.PinsReordered -= OnPinsReordered;
            }

            Unfollow(model);
            gone.Add(item);
        }

        foreach (var (_, item) in _wired) gone.Add(item);
        foreach (var item in _evicted) gone.Add(item);

        _evicted.Clear();
        _placed.Clear();
        _modelOfNode.Clear();
        _wired.Clear();
        _wireOf.Clear();

        if (_scene == null || gone.Count == 0) return;

        using (new OwnChange(this)) _scene.ResetWithout(gone);
    }

    // A node put in the collection: on the plane, with only its own wires - the rest of the graph is drawn already.
    private void Put(ICanvasNode model)
    {
        if (_scene == null || Place(model) is not { } item) return;

        // Brought back by an undo, its item may stand on the plane already, and the lines of its wires with it: those
        // are heard of at once, as anything drawn is, before its wires are drawn - so none is drawn twice.
        if (_scene.Holds(item)) OnSceneChanged(this, EventArgs.Empty);
        else using (new OwnChange(this)) _scene.Add(item);

        PutWiresOf(model);
        KeepWiresOnTop();
    }

    // The node's item, dressed in a control of its own and followed - not yet on the plane. Null for one placed already.
    private ElementItem Place(ICanvasNode model)
    {
        if (_scene == null || _placed.ContainsKey(model)) return null;

        // THE ITEM THE SCENE ALREADY HAS for this node, when there is one. The scene and its history outlive this canvas,
        // and a second item for the same node is the node twice once an undo puts the first one back.
        if (SceneItems.Of(_scene, model) is not ElementItem item)
        {
            item = new ElementItem(null, new Rect(model.Left, model.Top, Math.Max(0, model.Width), 0))
            {
                SizeFollowsContent = true,
                Model = model
            };

            SceneItems.Keep(_scene, model, item);
        }

        // A CONTROL OF ITS OWN, always: one built earlier was bound to what the node said then.
        if (_built.Remove(item, out var stale))
        {
            Discard(item, stale);
        }

        var node = Build(model);
        item.Element = node;
        _built[item] = node;
        _placed[model] = item;
        _modelOfNode[node] = model;

        // A SOCKET DRAGGED to another place along its side: the container says it happened, the MOVE is made here, in
        // the application's own list. Nothing happens to the wires - a wire sits on the socket, not on its place.
        node.PinsReordered += OnPinsReordered;

        Sockets(model, node);
        Follow(model);

        return item;
    }

    // THE WIRES OVER THE NODES, as the graph is always drawn: a node put in after them lands on top, and is put back
    // under them once the edits of the moment are done - in one change of the scene, not one per node.
    private void KeepWiresOnTop()
    {
        if (_wireOf.Count == 0 || _wiresOnTopPosted) return;

        _wiresOnTopPosted = true;
        LoopSignal.Post(_putWiresOnTop ??= PutWiresOnTop);
    }

    private bool _wiresOnTopPosted;
    private Action _putWiresOnTop;

    // The graph's nodes standing over its lowest line go down to just under it; everything else keeps its place - a
    // note put over a wire stays over it.
    private void PutWiresOnTop()
    {
        _wiresOnTopPosted = false;
        if (_scene == null || _wireOf.Count == 0) return;

        var items = _scene.Items;
        var lowest = -1;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is ConnectionItem line && _wireOf.ContainsKey(line))
            {
                lowest = index;
                break;
            }
        }

        if (lowest < 0) return;

        var over = new HashSet<ICanvasItem>();
        for (var index = lowest + 1; index < items.Count; index++)
        {
            if (items[index] is ElementItem node && Model(node) != null) over.Add(node);
        }

        // Already so - every node of the graph under its lines - nothing changes.
        if (over.Count == 0) return;

        var ordered = new List<ICanvasItem>(items.Count);
        for (var index = 0; index < lowest; index++) ordered.Add(items[index]);

        for (var index = lowest + 1; index < items.Count; index++)
        {
            if (over.Contains(items[index])) ordered.Add(items[index]);
        }

        for (var index = lowest; index < items.Count; index++)
        {
            if (!over.Contains(items[index])) ordered.Add(items[index]);
        }

        using (new OwnChange(this)) _scene.Reset(ordered);
    }

    private CanvasNode Build(ICanvasNode model)
    {
        var node = new CanvasNode { ContentTemplateSelector = _selector };

        // BOUND, not copied. Everything the container shows that the node says is a binding to the node's own object -
        // which is what bindings are for, and what keeps the two from being two answers to one question. What is left
        // below is the one thing a binding cannot do: turning a collection of sockets into a collection of pins.
        Bind(node, CanvasNode.KindProperty, model, nameof(ICanvasNode.Kind));
        Bind(node, CanvasNode.TitleProperty, model, nameof(ICanvasNode.Title));
        Bind(node, CanvasNode.IsCollapsedProperty, model, nameof(ICanvasNode.IsCollapsed));

        // ITS COLOR, through a converter: the node holds a color and the strip needs a brush, and the brush is made
        // per node here. A color nobody chose converts to nothing at all, which leaves the strip wearing the theme's.
        node.SetBinding(CanvasNode.AccentProperty, new Adamantium.UI.Core.Data.Binding(nameof(ICanvasNode.Accent))
        {
            Source = model,
            Mode = Adamantium.UI.Core.Data.BindingMode.OneWay,
            Converter = new ColorToBrushConverter()
        });
        Bind(node, ContentControl.ContentProperty, model, nameof(ICanvasNode.Specialization));

        return node;
    }

    private void OnPinsReordered(object sender, CanvasPinOrderEventArgs e)
    {
        if (sender is not CanvasNode node || !_modelOfNode.TryGetValue(node, out var model)) return;

        var sockets = e.IsInput ? model.Inputs : model.Outputs;
        if (e.From < 0 || e.From >= sockets.Count) return;

        var to = e.To < 0 ? 0 : e.To > sockets.Count - 1 ? sockets.Count - 1 : e.To;

        // ONE move. Taken out and put back would be a moment in which the socket is not on the node at all - and
        // everything downstream believes it: the pins are fitted to the sockets by position, so a pin would be
        // destroyed and made again, and the wires rebuilt against a node one socket short.
        sockets.Move(e.From, to);
    }

    private static void Bind(CanvasNode node, AdamantiumProperty property, ICanvasNode model, string path) =>
        node.SetBinding(property, new Adamantium.UI.Core.Data.Binding(path)
        {
            Source = model,
            Mode = Adamantium.UI.Core.Data.BindingMode.TwoWay
        });

    // The nodes taken out of the collection - one, or all of a Clear - off the plane in ONE change of the scene: a node
    // and each of its wires at a time is a walk of the scene each.
    private void Drop(IList models)
    {
        var dropped = new HashSet<ICanvasNode>();
        var gone = new HashSet<ICanvasItem>();
        foreach (var entry in models)
        {
            if (entry is not ICanvasNode model || !_placed.Remove(model, out var item)) continue;

            if (item.Element is CanvasNode going)
            {
                going.PinsReordered -= OnPinsReordered;
                _modelOfNode.Remove(going);
            }

            dropped.Add(model);
            gone.Add(item);
        }

        if (dropped.Count == 0) return;

        // A NODE LEAVING TAKES ITS WIRES, and both ends of each: the wire knows where it sits, so nothing has to walk
        // the graph looking for what pointed at this one. Cut held, so each is not heard and taken off on its own.
        var ends = new HashSet<ICanvasNode>();
        using (new OwnChange(this))
        {
            foreach (var model in dropped)
            {
                Gone(model.Inputs, gone, ends);
                Gone(model.Outputs, gone, ends);
                Cut(model.Inputs);
                Cut(model.Outputs);
                Unfollow(model);
            }

            _scene?.ResetWithout(gone);
        }

        foreach (var end in ends)
        {
            if (!dropped.Contains(end)) TakenOf(end);
        }
    }

    // The lines of a leaving node's wires, and the nodes at their ends.
    private void Gone(Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> sockets, HashSet<ICanvasItem> gone,
        HashSet<ICanvasNode> ends)
    {
        foreach (var socket in sockets)
        {
            foreach (var wire in socket.Connections)
            {
                if (wire.FromNode != null) ends.Add(wire.FromNode);
                if (wire.ToNode != null) ends.Add(wire.ToNode);
                if (!_wired.Remove(wire, out var line)) continue;

                _wireOf.Remove(line);
                gone.Add(line);
            }
        }
    }

    // The control goes with this canvas; the item stays with the scene and its history, for the next canvas to dress.
    private void Discard(ElementItem item, CanvasNode node)
    {
        node.PinsReordered -= OnPinsReordered;

        if (ReferenceEquals(item.Element, node))
        {
            item.Element = null;
        }

        DiscardedVisuals.PublishSubtree(node);
    }

    private static void Cut(Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> sockets)
    {
        foreach (var socket in sockets)
        {
            while (socket.Connections.Count > 0) socket.Connections[0].Disconnect();
        }
    }

    // A PIN PER SOCKET, which is the one thing here a binding cannot do: it is a collection turned into another
    // collection. Each pin is then bound to its socket, so a name or a kind edited in an inspector needs nothing of
    // this to run again.
    private void Sockets(ICanvasNode model, CanvasNode node)
    {
        Fit(node.InputPins, model.Inputs, true, node, _paint);
        Fit(node.OutputPins, model.Outputs, false, node, _paint);

        Watch(model.Inputs);
        Watch(model.Outputs);
    }

    private static void Fit(System.Collections.ObjectModel.ObservableCollection<CanvasNodePin> pins,
        Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> sockets, bool input, CanvasNode node,
        SocketKindToBrushConverter paint)
    {
        if (input) node.Inputs = sockets.Count;
        else node.Outputs = sockets.Count;

        for (var i = 0; i < pins.Count && i < sockets.Count; i++)
        {
            pins[i].SetBinding(CanvasNodePin.NameProperty, new Adamantium.UI.Core.Data.Binding(nameof(ICanvasSocket.Name))
            {
                Source = sockets[i],
                Mode = Adamantium.UI.Core.Data.BindingMode.TwoWay
            });

            pins[i].SetBinding(CanvasNodePin.KindProperty, new Adamantium.UI.Core.Data.Binding(nameof(ICanvasSocket.Kind))
            {
                Source = sockets[i],
                Mode = Adamantium.UI.Core.Data.BindingMode.TwoWay
            });

            // HOW MANY WIRES it takes, on the pin as well - the gesture that draws one works on pins and would
            // otherwise have to invent the rule itself, which is how "one per input" got hard-coded there.
            pins[i].SetBinding(CanvasNodePin.CapacityProperty,
                new Adamantium.UI.Core.Data.Binding(nameof(ICanvasSocket.Capacity))
                {
                    Source = sockets[i],
                    Mode = Adamantium.UI.Core.Data.BindingMode.TwoWay
                });

            // ...and WHAT IT LOOKS LIKE, read off what flows through it. Bound to the kind rather than set once, so a
            // socket told it now carries a color changes color; a kind the catalog says nothing about leaves the
            // pin wearing the theme's.
            if (paint == null) continue;

            pins[i].SetBinding(CanvasNodePin.ColorProperty,
                new Adamantium.UI.Core.Data.Binding(nameof(ICanvasSocket.Kind))
                {
                    Source = sockets[i],
                    Mode = Adamantium.UI.Core.Data.BindingMode.OneWay,
                    Converter = paint
                });
        }
    }

    // A wire made or cut by the APPLICATION is a line appearing or going on the plane, and the only place that says so
    // is the socket it sits on.
    private void Watch(Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> sockets)
    {
        foreach (var socket in sockets)
        {
            if (!_watched.Add(socket)) continue;

            socket.Connections.CollectionChanged += OnWiresChanged;
        }
    }

    // Only the wires made or cut: each is heard on both its sockets, and drawn or taken off once.
    private void OnWiresChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (_reconciling || _scene == null) return;

        if (e.Action is NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Replace)
        {
            RebuildWires();
            return;
        }

        foreach (var item in e.OldItems ?? Array.Empty<object>())
        {
            if (item is not CanvasConnection wire) continue;

            TakeWire(wire);
            TakenOf(wire.FromNode);
            TakenOf(wire.ToNode);
        }

        foreach (var item in e.NewItems ?? Array.Empty<object>())
        {
            if (item is not CanvasConnection wire) continue;

            PutWire(wire);
            TakenOf(wire.FromNode);
            TakenOf(wire.ToNode);
        }
    }

    // ...and a socket ADDED or DROPPED is a pin appearing or going, which no binding does: the two collections have to
    // be brought level again.
    private void Follow(ICanvasNode model)
    {
        model.Inputs.CollectionChanged += OnSocketsChanged;
        model.Outputs.CollectionChanged += OnSocketsChanged;
        model.PropertyChanged += OnModelChanged;
        _modelOfSockets[model.Inputs] = model;
        _modelOfSockets[model.Outputs] = model;
    }

    private void Unfollow(ICanvasNode model)
    {
        model.Inputs.CollectionChanged -= OnSocketsChanged;
        model.Outputs.CollectionChanged -= OnSocketsChanged;
        model.PropertyChanged -= OnModelChanged;
        _modelOfSockets.Remove(model.Inputs);
        _modelOfSockets.Remove(model.Outputs);

        Forget(model.Inputs);
        Forget(model.Outputs);
    }

    private void Forget(Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> sockets)
    {
        foreach (var socket in sockets)
        {
            if (_watched.Remove(socket)) socket.Connections.CollectionChanged -= OnWiresChanged;
        }
    }

    private void OnSocketsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // A MOVED socket is reported as leaving and arriving at once, and it is still there - letting go of it here
        // would stop the wires on it being heard of again.
        if (e.Action != NotifyCollectionChangedAction.Move && e.OldItems != null)
        {
            foreach (var entry in e.OldItems)
            {
                if (entry is ICanvasSocket socket && _watched.Remove(socket))
                {
                    socket.Connections.CollectionChanged -= OnWiresChanged;
                }
            }
        }

        if (sender == null || !_modelOfSockets.TryGetValue(sender, out var model) ||
            !_placed.TryGetValue(model, out var item))
        {
            return;
        }

        // Put on at the END, no pin stands for another socket: the lines stay as they are, and only the new sockets'
        // wires are drawn.
        if (e.Action == NotifyCollectionChangedAction.Add && sender is ICollection list && e.NewItems != null &&
            e.NewStartingIndex == list.Count - e.NewItems.Count)
        {
            if (item.Element is CanvasNode grown) Sockets(model, grown);

            var fresh = new List<ICanvasItem>();
            foreach (var entry in e.NewItems)
            {
                if (entry is not ICanvasSocket socket) continue;

                foreach (var wire in socket.Connections)
                {
                    if (Wire(wire) is { } line) fresh.Add(line);
                }
            }

            using (new OwnChange(this)) _scene?.ResetWithout(null, fresh);

            TakenAround(model);
            return;
        }

        // Otherwise only this node's wires are drawn again, in one change of the scene: its pins are fitted to its
        // sockets by position, so a line may stand on a pin that is another socket's now. The rest of the graph is not
        // touched, and a line drawn again looks as it was drawn.
        var was = new Dictionary<CanvasConnection, ConnectionItem>();
        var old = ForgetLinesOf(model, e, was);
        if (item.Element is CanvasNode node) Sockets(model, node);

        var lines = new List<ICanvasItem>();
        LinesOf(model.Inputs, lines);
        LinesOf(model.Outputs, lines);
        LookAsBefore(lines, was);

        using (new OwnChange(this)) _scene?.ResetWithout(old, lines);

        TakenAround(model);
    }

    // Each line drawn again in the look its wire's line had: width, color, routing.
    private void LookAsBefore(List<ICanvasItem> lines, Dictionary<CanvasConnection, ConnectionItem> was)
    {
        foreach (var drawnAgain in lines)
        {
            if (drawnAgain is not ConnectionItem line || !_wireOf.TryGetValue(line, out var wire) ||
                !was.TryGetValue(wire, out var before))
            {
                continue;
            }

            line.Thickness = before.Thickness;
            line.Stroke = before.Stroke;
            line.Routes = before.Routes;
        }
    }

    // The lines of a node's wires, let go of: those on its sockets now and on those just taken from it - or, told only
    // that the list was reset, every line reaching the node - each kept by its wire, for its look.
    private HashSet<ICanvasItem> ForgetLinesOf(ICanvasNode model, NotifyCollectionChangedEventArgs e,
        Dictionary<CanvasConnection, ConnectionItem> was)
    {
        var wires = new List<CanvasConnection>();
        Collect(model.Inputs, wires);
        Collect(model.Outputs, wires);

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var (wire, _) in _wired)
            {
                if (ReferenceEquals(wire.FromNode, model) || ReferenceEquals(wire.ToNode, model)) wires.Add(wire);
            }
        }
        else
        {
            foreach (var entry in e.OldItems ?? Array.Empty<object>())
            {
                if (entry is ICanvasSocket socket) wires.AddRange(socket.Connections);
            }
        }

        var lines = new HashSet<ICanvasItem>();
        foreach (var wire in wires)
        {
            if (!_wired.Remove(wire, out var line)) continue;

            _wireOf.Remove(line);
            lines.Add(line);
            was[wire] = line;
        }

        return lines;
    }

    private static void Collect(Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> sockets,
        List<CanvasConnection> into)
    {
        foreach (var socket in sockets) into.AddRange(socket.Connections);
    }

    // The new lines of the wires on these sockets whose other end is on the plane, into the list.
    private void LinesOf(Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> sockets, List<ICanvasItem> into)
    {
        foreach (var socket in sockets)
        {
            foreach (var wire in socket.Connections)
            {
                if (Wire(wire) is { } line) into.Add(line);
            }
        }
    }

    // Nothing is read out of the node here and nothing is written onto the container: the place a node was moved to is
    // already the item's, and the scene is simply told that a rectangle on it is not where it was. Without it a node
    // moved from a field stays drawn where it stood, because the plane is laid out from the items and nobody asked it
    // to do that again.
    private void OnModelChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ICanvasNode.Left):
            case nameof(ICanvasNode.Top):
            case nameof(ICanvasNode.Width):
                // The host's own word: nothing was drawn or cut, so the wires are not looked for again.
                using (new OwnChange(this)) (_scene as CanvasScene)?.Touch();
                break;

            // TOLD IT IS NOW A TEXTURE, it becomes one: the word changed, and what that word means is in the catalog
            // the canvas was handed. Here and not in the node, because the node has no catalog - and not only where
            // the palette makes one, or the same pick from an inspector would change the label and nothing else.
            case nameof(ICanvasNode.Kind) when sender is ICanvasNode model:
                model.Specialization = KindOf(model.Kind)?.Create();
                break;
        }
    }

    // Nothing about a node is copied back here: its place is the model's own field, written through the item, and what
    // the container shows is bound. What IS left is the wires, which nothing on the plane writes into the graph by
    // itself - a gesture leaves one lying there, and the graph has to hear about it.
    private void OnSceneChanged(object sender, EventArgs e)
    {
        if (_reconciling) return;

        using (new OwnChange(this)) Reconcile();
    }

    // WIRES DRAWN ON THE PLANE, into the graph - and wires cut on it, out of it. A wire is made by a gesture that knows
    // nothing about the application's objects, so what it leaves behind is found here and joined properly; seating
    // itself in both sockets is the wire's own business, so there is nothing here to keep in step.
    private void Reconcile()
    {
        if (_scene == null) return;

        // In paint order, so which of two lines into a full socket wins is the order they stand in.
        var lines = new List<ConnectionItem>();
        var drawn = new HashSet<ConnectionItem>();
        foreach (var item in _scene.Items)
        {
            if (item is not ConnectionItem wire) continue;

            lines.Add(wire);
            drawn.Add(wire);
        }

        // Gone from the plane: let go of first, not cut yet - a line put back by an undo may stand for the same wire.
        var lost = new List<CanvasConnection>();
        foreach (var (model, item) in _wired)
        {
            if (!drawn.Contains(item)) lost.Add(model);
        }

        foreach (var model in lost)
        {
            if (_wired.Remove(model, out var line)) _wireOf.Remove(line);
        }

        // New on the plane: somebody drew it, and the graph has not heard yet - or an undo put back the line of a wire the
        // graph still has and draws nowhere, which is that wire's line again.
        foreach (var item in lines)
        {
            if (_wireOf.ContainsKey(item) || _evicted.Contains(item)) continue;

            if (Model(item.FromItem) is not { } from || Model(item.ToItem) is not { } to) continue;
            if (Socket(from, item.FromItem, item.FromPin, false) is not { } fromSocket) continue;
            if (Socket(to, item.ToItem, item.ToPin, true) is not { } toSocket) continue;

            if (Undrawn(fromSocket, toSocket) is { } had)
            {
                _wired[had] = item;
                _wireOf[item] = had;
                continue;
            }

            // A full socket gives up its oldest wire to the new one: that wire's line goes with it.
            var before = new List<CanvasConnection>(fromSocket.Connections);
            before.AddRange(toSocket.Connections);

            var wire = new CanvasConnection(fromSocket, toSocket);
            _wired[wire] = item;
            _wireOf[item] = wire;

            foreach (var old in before)
            {
                if (fromSocket.Connections.Contains(old) || toSocket.Connections.Contains(old)) continue;
                if (_wired.Remove(old, out var line) && _wireOf.Remove(line)) _evicted.Add(line);

                TakenOf(old.FromNode);
                TakenOf(old.ToNode);
            }
        }

        // A wire whose line went and no line took up again was cut: the graph no longer has it.
        foreach (var model in lost)
        {
            if (!_wired.ContainsKey(model)) model.Disconnect();
        }

        // Taken off once the scene has told everyone of this change - not from inside its telling.
        if (_evicted.Count > 0 && !_evictedPosted)
        {
            _evictedPosted = true;
            LoopSignal.Post(_takeEvicted ??= TakeEvicted);
        }
    }

    private readonly HashSet<ICanvasItem> _evicted = new();
    private bool _evictedPosted;
    private Action _takeEvicted;

    // Also before the host lets go of the scene, which outlives it: a line left there would be taken for one drawn.
    private void TakeEvicted()
    {
        _evictedPosted = false;
        if (_scene != null && _evicted.Count > 0)
        {
            using (new OwnChange(this)) _scene.ResetWithout(_evicted);
        }

        _evicted.Clear();
    }

    // The graph's wire from one socket to the other that no line on the plane stands for, when it has one.
    private CanvasConnection Undrawn(ICanvasSocket from, ICanvasSocket to)
    {
        foreach (var wire in from.Connections)
        {
            if (ReferenceEquals(wire.To, to) && !_wired.ContainsKey(wire)) return wire;
        }

        return null;
    }

    /// <summary>The container standing for that node, or null while there is none.</summary>
    public ElementItem ItemOf(ICanvasNode node) =>
        node != null && _placed.TryGetValue(node, out var item) ? item : null;

    private ICanvasNode Model(ElementItem item) =>
        item?.Model is ICanvasNode model && _placed.TryGetValue(model, out var placed) && ReferenceEquals(placed, item)
            ? model
            : null;

    // THE HOST'S OWN CHANGE of the scene, held from Reconcile while it lasts: the scene answers back, and what the host
    // puts in or takes out is not somebody drawing or cutting a wire.
    private readonly ref struct OwnChange
    {
        private readonly CanvasGraphHost _host;
        private readonly bool _held;

        public OwnChange(CanvasGraphHost host)
        {
            _host = host;
            _held = host._reconciling;
            host._reconciling = true;
        }

        public void Dispose() => _host._reconciling = _held;
    }

    // The mirror of Pin: the model's socket standing for the container's pin, found by where it sits.
    private static ICanvasSocket Socket(ICanvasNode model, ElementItem item, CanvasNodePin pin, bool input)
    {
        if (item.Element is not CanvasNode node || pin == null) return null;

        var pins = input ? node.InputPins : node.OutputPins;
        var sockets = input ? model.Inputs : model.Outputs;

        var at = pins.IndexOf(pin);

        return at >= 0 && at < sockets.Count ? sockets[at] : null;
    }
}
