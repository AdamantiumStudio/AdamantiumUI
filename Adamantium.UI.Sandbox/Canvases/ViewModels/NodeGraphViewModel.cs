using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Core.Collections;
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

/// <summary>Node graph tab: nodes and the wires between their sockets on an unbounded plane, and a graph that computes
/// something from the start.</summary>
[ViewModel]
public partial class NodeGraphViewModel : CanvasPageViewModel
{
    public NodeGraphViewModel() : base("NodeGraph", "node-graph-layout.json")
    {
        Tools.Add(NodeTool);

        // A GRAPH THAT WORKS, there from the start: two colors and an amount go into a mix, the mix is dimmed, and the
        // output shows what came out. Not behind a switch, because it is the point - a graph that computes something is
        // the difference between this page and a picture of a node editor.
        Seed();

        _graph = new CanvasGraphRunner(Nodes);

        Restore();
    }

    /// <summary>The graph's node tool: a press asks which kind from the page's list, and the pick adds the node to the
    /// page's collection.</summary>
    public ICanvasTool NodeTool { get; } = new NodeTool { Description = "put a node on the plane" };

    /// <summary>THE GRAPH: this page's own objects. The graph shows them and writes into them - a node made on the plane
    /// arrives here, one deleted leaves - so this is the whole account of the graph and there is no second copy of it
    /// anywhere.</summary>
    public TrackingCollection<ICanvasNode> Nodes { get; } = new();

    /// <summary>The kinds this page offers. Given to the graph, so the palette, the inspector's drop-down and the loader
    /// all read the one list - and each entry knows how to make the inside of a node of its sort, which is why the graph
    /// needs no factory handed to it.</summary>
    public IReadOnlyList<ICanvasNodeKind> NodeKinds => NodeSet.Kinds;

    /// <summary>WHICH CATALOG this page is holding. A page doing two jobs holds two of these and binds the one it wants -
    /// the graph is handed a list of kinds and knows nothing about sets; a saved graph records the name.</summary>
    public Models.GraphNodeSet NodeSet { get; } = Models.GraphNodeKind.Set;

    private ICanvasNode Node(string kind, double left, double top)
    {
        var entry = NodeSet.Named(kind);

        var made = new CanvasNodeViewModel
        {
            Kind = kind,
            Title = entry?.Title ?? kind,
            Left = left,
            Top = top,
            Accent = entry?.Accent,
            Specialization = entry?.Create()
        };

        Nodes.Add(made);

        return made;
    }

    // WHAT KEEPS THE GRAPH WORKED OUT. One line, because none of it is this page's business: which node is owed a
    // value, in what order they go, when a pass is worth starting and when the one running is already answering a
    // question nobody is asking any more. All this page says is what a node computes - see NodeSpecialization.
    private readonly CanvasGraphRunner _graph;

    // THE GRAPH THE PAGE OPENS WITH: two colors mixed by an amount, the mix dimmed, and the result shown. Small enough
    // to take in at a glance and complete enough to be worth changing - which is what a demonstration has to be.
    private void Seed()
    {
        var warm = Node(Models.GraphWords.Kinds.Color, -520, 40);
        var cool = Node(Models.GraphWords.Kinds.Color, -520, 170);
        var amount = Node(Models.GraphWords.Kinds.Number, -520, 300);
        var mix = Node(Models.GraphWords.Kinds.Mix, -260, 120);
        var dim = Node(Models.GraphWords.Kinds.Number, -260, 320);
        var scale = Node(Models.GraphWords.Kinds.Scale, -20, 160);
        var output = Node(Models.GraphWords.Kinds.Output, 240, 160);

        if (warm.Specialization is ColorSpecialization first)
        {
            first.Color = Colors.OrangeRed;
        }

        if (cool.Specialization is ColorSpecialization second)
        {
            second.Color = Colors.DeepSkyBlue;
        }

        if (dim.Specialization is NumberSpecialization brightness)
        {
            brightness.Value = 1;
        }

        _ = new CanvasConnection(warm.Outputs[0], mix.Inputs[0]);
        _ = new CanvasConnection(cool.Outputs[0], mix.Inputs[1]);
        _ = new CanvasConnection(amount.Outputs[0], mix.Inputs[2]);
        _ = new CanvasConnection(mix.Outputs[0], scale.Inputs[0]);
        _ = new CanvasConnection(dim.Outputs[0], scale.Inputs[1]);
        _ = new CanvasConnection(scale.Outputs[0], output.Inputs[0]);
    }

    /// <summary>What a SOCKET may carry. Offered as a list for the same reason the node's kind is: two sockets are
    /// joined when their kinds agree, so a mistyped one is a wire that silently refuses to go on. The empty entry is
    /// first and means "anything", which is what a socket that has not been told says.</summary>
    public IReadOnlyList<ICanvasSocketKind> SocketKinds { get; } = Models.GraphSocketKind.All;

    protected override void SelectionMoved()
    {
        RaisePropertyChanged(nameof(FrameSection));
        RaisePropertyChanged(nameof(NodeSection));
    }

    /// <summary>The node's OWN lines, under the ones every control has. A node is an ElementItem like a button is, so
    /// the section above already gives it a place and a size; what this adds is the handful of things that make it a
    /// node - what it is called, how many sockets down each side, and the two colors it is read by.</summary>
    public Visibility NodeSection => ShownElement<CanvasNode>();

    public Visibility FrameSection => Shown<CanvasFrameItem>();

    protected override bool Describable => base.Describable || FrameSection == Visibility.Visible;

    /// <summary>Puts one more socket on the selected node, down the side whose plus was pressed. It carries no kind:
    /// what flows through it is the next thing to say, and the row below says it.</summary>
    [Command]
    private void AddSocket(object side)
    {
        if (Selection == null) return;

        foreach (var item in Selection)
        {
            if (item is not ElementItem { Model: ICanvasNode node }) continue;

            var input = (side as string) != Models.GraphWords.Sides.Out;
            var sockets = input ? node.Inputs : node.Outputs;
            var prefix = input ? Models.GraphWords.Sides.In : Models.GraphWords.Sides.Out;

            sockets.Add(new CanvasSocketViewModel { Name = $"{prefix} {sockets.Count + 1}" });
            return;
        }
    }

    /// <summary>Removes a socket from its node, found among the selection; asks for confirmation like any delete, since
    /// its wires go with it.</summary>
    [Command]
    private async Task RemoveSocket(object which)
    {
        if (which is not ICanvasSocket socket || Selection == null) return;

        ICanvasNode holder = null;
        foreach (var item in Selection)
        {
            if (item is not ElementItem { Model: ICanvasNode node }) continue;
            if (!node.Inputs.Contains(socket) && !node.Outputs.Contains(socket)) continue;

            holder = node;
            break;
        }

        if (holder == null) return;

        if (AsksBeforeDelete)
        {
            var dialogs = UIApplication.Current?.Container?.Resolve<IDialogService>();
            if (dialogs != null)
            {
                var result = await dialogs.ShowDialogAsync<ConfirmDialogViewModel>(new NavigationParameters()
                    .Add("title", CanvasPageStrings.RemoveSocketTitle)
                    .Add("message", CanvasPageStrings.RemoveSocketQuestion(socket.Name, holder.Title)));

                if (result.Result != DialogButtonResult.Ok) return;
            }
        }

        if (!holder.Inputs.Remove(socket)) holder.Outputs.Remove(socket);
    }

    /// <summary>What has been typed into the list of kinds. The list itself, when it is open and where, is the graph's -
    /// see its PaletteFace and PickedKind; this is only the narrowing.</summary>
    [Bindable] private string _nodeSearch = string.Empty;

    partial void OnNodeSearchChanged(string value)
    {
        RaisePropertyChanged(nameof(FoundKinds));
        RaisePropertyChanged(nameof(FoundGroups));
        RaisePropertyChanged(nameof(SearchFace));
    }

    /// <summary>The palette's sections, each holding its kinds - what the list actually shows. Built from the kinds the
    /// search leaves, so a section with nothing left in it simply is not there.</summary>
    public IReadOnlyList<Models.GraphKindGroup> FoundGroups
    {
        get
        {
            var groups = new List<Models.GraphKindGroup>();
            var byName = new Dictionary<string, List<ICanvasNodeKind>>();

            foreach (var kind in FoundKinds)
            {
                var name = string.IsNullOrEmpty(kind.Group) ? "Other" : kind.Group;

                if (byName.TryGetValue(name, out var family))
                {
                    family.Add(kind);
                    continue;
                }

                byName[name] = [kind];
                groups.Add(new Models.GraphKindGroup(name, byName[name], true));
            }

            return groups;
        }
    }

    /// <summary>The cross that empties the search, shown only while there is something to empty: a cross on an empty
    /// field offers to undo nothing.</summary>
    public Visibility SearchFace =>
        string.IsNullOrEmpty(NodeSearch) ? Visibility.Collapsed : Visibility.Visible;

    [Command]
    private void ClearNodeSearch() => NodeSearch = string.Empty;

    /// <summary>The kinds the search leaves. Everything when nothing has been typed, which is what a list opened by a
    /// gesture should show first - the search is for narrowing, not for making the list appear.</summary>
    public IReadOnlyList<ICanvasNodeKind> FoundKinds
    {
        get
        {
            var found = new List<ICanvasNodeKind>();
            foreach (var kind in NodeKinds)
            {
                if (string.IsNullOrWhiteSpace(NodeSearch)
                    || kind.Title.Contains(NodeSearch, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(kind);
                }
            }

            // BY SECTION, then by name inside it. The list is read top to bottom by somebody looking for a sort of node,
            // and sorts of node are what the sections are.
            found.Sort((left, right) =>
            {
                var section = string.CompareOrdinal(left.Group, right.Group);

                return section != 0 ? section : string.CompareOrdinal(left.Title, right.Title);
            });

            return found;
        }
    }

    /// <summary>Shuts the list without choosing. Closing is the graph's state, the narrowing is the page's, so this
    /// clears both.</summary>
    [Command]
    private void CloseNodes(object which)
    {
        if (which is NodeGraph graph) graph.IsPaletteOpen = false;

        NodeSearch = string.Empty;
    }

    /// <summary>Where the graph is kept between runs.</summary>
    public string GraphPath { get; set; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "adamantium-graph.json");

    /// <summary>What the last save or load did, in words, so the stand says something rather than appearing to do
    /// nothing.</summary>
    [Bindable] private string _graphStatus = "not saved yet";

    /// <summary>Draws a comment frame round what is selected. The graph makes it and names it in its own words with a
    /// word a person will replace at once - which is better than an empty strip that looks broken.</summary>
    [Command]
    private void FrameSelection(object which)
    {
        if (which is NodeGraph graph) graph.FrameSelection();
    }

    // The GRAPH is handed in by the view. The page does not hold one - a view-model that reached for a control would be
    // a view-model that has to be given one before it can answer anything - and the button that saves is standing next
    // to it anyway.
    [Command]
    private void SaveGraph(object which)
    {
        if (which is not NodeGraph graph) return;

        var path = Asked(FileDialog.Save(new SaveFileRequest
        {
            Title = CanvasPageStrings.SaveGraph,
            FileName = System.IO.Path.GetFileName(GraphPath),
            DefaultExtension = "json",
            FileTypes = GraphFiles,
            Key = "sandbox.graph.save",
            Owner = graph.GetWindow()?.Handle ?? IntPtr.Zero
        }));

        if (path == null) return;

        try
        {
            System.IO.File.WriteAllText(path, Models.GraphFile.Write(Nodes, NodeSet));
            GraphPath = path;
            GraphStatus = $"saved to {path}";
        }
        catch (System.IO.IOException e)
        {
            GraphStatus = $"could not save: {e.Message}";
        }
    }

    [Command]
    private void LoadGraph(object which)
    {
        if (which is not NodeGraph graph) return;

        var path = Asked(FileDialog.Open(new OpenFileRequest
        {
            Title = CanvasPageStrings.OpenGraph,
            FileTypes = GraphFiles,
            Key = "sandbox.graph.open",
            Owner = graph.GetWindow()?.Handle ?? IntPtr.Zero
        }));

        if (path == null) return;

        try
        {
            if (!System.IO.File.Exists(path))
            {
                GraphStatus = "there is no such file";
                return;
            }

            GraphPath = path;
            GraphStatus = Models.GraphFile.Read(System.IO.File.ReadAllText(path), Nodes, NodeSet)
                          ?? $"loaded from {path}";

            graph.FitAll();
        }
        catch (System.IO.IOException e)
        {
            GraphStatus = $"could not load: {e.Message}";
        }
    }

    // Built at each ask, so the dialog names the files in the language of the moment.
    private static IReadOnlyList<FileType> GraphFiles => [new FileType(CanvasPageStrings.GraphFiles, "json")];

    // A dialog that cannot be shown is not a canceled one, and the difference matters: canceling is the user's answer
    // and needs no report, while a platform with no dialog would leave the button doing nothing at all. Then the path
    // that was remembered stands in for the question.
    private string Asked(string chosen)
    {
        if (chosen != null) return chosen;
        if (FileDialog.IsAvailable) return null;

        GraphStatus = "there is nowhere to ask, so the remembered file is used";
        return GraphPath;
    }
}
