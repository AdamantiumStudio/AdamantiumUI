using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Controls.Panels;

namespace Adamantium.UI.Controls.Docking;

/// <summary>
/// A whole docking layout: a FOREST of roots (the main window plus every floating one), and the operations that change
/// it. Both the gestures and any code go through these same methods - there is deliberately no second path, because two
/// paths drift apart and then it matters which one a save reads.
/// </summary>
public class DockingLayout
{
    /// <summary>Format version, written to the saved file. A layout outlives the code that wrote it.</summary>
    public int Version { get; set; } = 1;

    public List<DockingRoot> Roots { get; } = new();

    /// <summary>The main window's document well: everything inside is a document, outside a tool. A place, not a flag, so it
    /// survives being emptied; a node, since it splits within itself.</summary>
    public PaneNode DocumentWell
    {
        get => Main?.DocumentWell;
        set
        {
            if (Main is { } main) main.DocumentWell = value;
        }
    }

    /// <summary>The document area of the WINDOW this node is in, or null.</summary>
    private PaneNode WellOf(PaneNode node) => RootOf(node)?.DocumentWell;

    /// <summary>Whether a node is a document: inside the document well of its own root. Decides a group's look, what closing
    /// means and whether it may fold away.</summary>
    public bool IsDocument(PaneNode node)
    {
        if (node == null) return false;

        var well = WellOf(node);
        if (well == null) return false;

        for (var walk = node; walk != null; walk = walk.Parent)
        {
            if (ReferenceEquals(walk, well)) return true;
        }

        return false;
    }

    /// <summary>The document group a new document lands in: the one holding <paramref name="activePaneId"/>, else the first.
    /// Null while there is no document area.</summary>
    public PaneGroupNode ActiveWellGroup(string activePaneId)
    {
        if (DocumentWell == null) return null;
        if (DocumentWell is PaneGroupNode single) return single;

        PaneGroupNode first = null;
        PaneGroupNode firstNonEmpty = null;
        foreach (var group in GroupsIn(DocumentWell))
        {
            if (activePaneId != null && group.PaneIds.Contains(activePaneId)) return group;
            first ??= group;
            if (!group.IsEmpty) firstNonEmpty ??= group;
        }

        // Nothing active INSIDE the documents (the active pane is a tool, or there is none): fall back to where a
        // document would have gone before - the first group that holds anything.
        return firstNonEmpty ?? first;
    }

    private bool IsWell(PaneNode node) => node != null && ReferenceEquals(WellOf(node), node);

    /// <summary>The document area must never lose its LAST group. Emptied, it stays as empty space - but
    /// once it has been split, its other groups are ordinary: they may be torn off and they die when emptied, exactly
    /// like closing one of two editors side by side.</summary>
    private bool IsLastWellGroup(PaneGroupNode group) => IsLastWellGroup(RootOf(group), group);

    // Taking the ROOT rather than looking it up: Normalize walks one root at a time and knows which, and asking again
    // per node would search every tree in the layout for a node it is holding in its hand.
    private static bool IsLastWellGroup(DockingRoot root, PaneGroupNode group)
    {
        if (root?.DocumentWell == null || !Holds(root.DocumentWell, group)) return false;

        var count = 0;
        foreach (var _ in GroupsIn(root.DocumentWell))
        {
            if (++count > 1) return false;
        }

        return true;
    }

    /// <summary>What a panel docking BESIDE the document area is worth when nobody states a size - pushed by
    /// <see cref="DockingArea"/> from its EdgeDockSize, so a drop makes the band its preview drew.</summary>
    public PaneLength BandLength { get; set; } = PaneLength.Pixels(200);

    // What the caller asked for, or an even split. A BAND is what the callers who mean one pass in - an edge anchor,
    // a pane opened from code - and that is now the only way to become the centre's NEIGHBOUR: a drop aimed into the
    // document area splits the area itself, and two editors side by side share what one of them had.
    private static PaneLength? BandFor(PaneNode target, PaneLength? stated) => stated;

    /// <summary>Docks a group that was NOT in the layout before - a region opening a tool, a pane added by code - taking
    /// a BAND rather than half of what is there. Halving belongs to a drop aimed at a panel's side, where the one doing
    /// it can see what it costs.</summary>
    public void DockBeside(PaneNode target, DockZone side, PaneNode inserted, PaneLength? size = null)
    {
        SplitWithLength(target, side, inserted, BandFor(target, size) ?? BandLength);
    }

    public DockingRoot Main
    {
        get
        {
            foreach (var root in Roots)
            {
                if (root.IsMain) return root;
            }
            return null;
        }
    }

    /// <summary>Builds a layout from AUTHORED ZONES. The centre is laid first and the rest docked around it in
    /// declaration order, so the last edge declared is the outermost - as reading the markup suggests. This is why
    /// markup carries no fractions: a share written by hand is a share of a split the author cannot see.</summary>
    public static DockingLayout FromZones(IEnumerable<ZoneDeclaration> declarations)
    {
        var layout = new DockingLayout();
        var root = new DockingRoot(null, isMain: true);
        layout.Roots.Add(root);

        // The centre column: what the documents occupy and what a top/bottom band divides. It grows into the split node
        // holding the bands, never into the sides - which is what keeps those full height.
        PaneNode centre = null;

        foreach (var declaration in declarations)
        {
            var group = declaration.Group;

            if (root.Content == null)
            {
                // The first one takes everything and is where the documents live, whatever zone it named: a layout has
                // to start somewhere, and docking against nothing means nothing. From here on it is a PLACE.
                root.Content = group;
                centre = group;
                root.DocumentWell = group;
                continue;
            }

            if (declaration.Zone is DockZone.Center or DockZone.Floating)
            {
                // Center means "with the documents", not "split something": join the group that is already there.
                if (root.Content is PaneGroupNode documents)
                {
                    foreach (var pane in group.PaneIds) documents.Add(pane);
                    continue;
                }
            }

            var zone = declaration.Zone is DockZone.Center or DockZone.Floating ? DockZone.Right : declaration.Zone;

            // WHAT gets split is not always the whole layout. A SIDE takes the full height of the window, so it splits the
            // root; a TOP/BOTTOM band belongs under the documents and must not run beneath the sides, so it splits the
            // CENTRE COLUMN instead. That ordering is the layout every editor uses - sides first, the band gets what is
            // left - and stating it here makes it independent of the order the author happens to declare the zones in.
            var target = zone is DockZone.Top or DockZone.Bottom ? centre : root.Content;
            layout.Split(target, zone, group);

            // The band is now part of the centre column, so the NEXT band splits that column, not the group inside it.
            if (zone is DockZone.Top or DockZone.Bottom) centre = group.Parent ?? centre;

            // AFTER the split, which hands both sides a share of the target - right for a dropped pane, wrong for one
            // the author sized. A number in markup is PIXELS along the zone's own axis.
            if (!double.IsNaN(declaration.Size)) group.Length = PaneLength.Pixels(declaration.Size);
        }

        layout.Normalize();
        return layout;
    }

    // Splits the target and gives the arrival a length OF ITS OWN, leaving the target as it was. Halving is right when
    // the arrival takes a share of the target and wrong when it states its own size - nothing is taken FROM the target
    // and the stars absorb the difference. Measured: three restores from an edge strip cut the layout to 22 pixels.
    private void SplitWithLength(PaneNode target, DockZone side, PaneNode inserted, PaneLength length, bool nest = false)
    {
        var kept = target.Length;
        Split(target, side, inserted, nest: nest);
        inserted.Length = length;
        target.Length = kept;
    }

    // Cuts a length in two: the pair together is worth what the one of them was, so the row around them does not move.
    private static PaneLength Halve(PaneLength length, double keep, out PaneLength arrivals)
    {
        arrivals = new PaneLength(length.Value * (1 - keep), length.Unit);
        return new PaneLength(length.Value * keep, length.Unit);
    }

    /// <summary>Splits <paramref name="target"/>, putting <paramref name="inserted"/> on the given side of it and
    /// giving the newcomer <paramref name="fraction"/> of the space they now share.</summary>
    /// <param name="nest">Force a split node of its own even when the row runs the right way, so a split inside the document
    /// area does not swallow the row's other panels.</param>
    public void Split(PaneNode target, DockZone side, PaneNode inserted, double fraction = 0.5, bool nest = false)
    {
        var vertical = side is DockZone.Top or DockZone.Bottom;
        var before = side is DockZone.Left or DockZone.Top;

        // Already splitting the right way? One more child, NOT a nested split - that is what keeps the layout from
        // growing a level every time something is dropped on the same side.
        if (!nest && target.Parent is { } parent && parent.Orientation == (vertical ? Orientation.Vertical : Orientation.Horizontal))
        {
            var at = parent.Children.IndexOf(target);

            // The pair shares what the TARGET had, in its own unit (160px becomes 80+80, a weight of 2 becomes 1+1), so
            // nobody else in the row moves. A share of the WHOLE row instead took far more than the half it landed on.
            target.Length = Halve(target.Length, 1 - fraction, out var arrivals);
            inserted.Length = arrivals;

            parent.Insert(before ? at : at + 1, inserted);
            return;
        }

        var split = new PaneSplitNode { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal };
        var host = target.Parent;

        // The split stands where the target stood, so it inherits its claim on the OUTER row ("the console is 160 tall"
        // still holds when it is two panes side by side). Inside, the pair shares in WEIGHTS: a pixel number stated for
        // one axis says nothing about the other, and spending it there charged a height as a width.
        var kept = target.Length;
        target.Length = PaneLength.Stars(1 - fraction);
        inserted.Length = PaneLength.Stars(fraction);

        if (host != null)
        {
            host.Replace(target, split);
        }
        else
        {
            // target was a root's top node - the new split takes its place there.
            foreach (var root in Roots)
            {
                if (ReferenceEquals(root.Content, target)) root.Content = split;
            }
        }

        split.Length = kept;

        if (before)
        {
            split.Add(inserted);
            split.Add(target);
        }
        else
        {
            split.Add(target);
            split.Add(inserted);
        }
    }

    /// <summary>Moves a pane into <paramref name="target"/> as a tab (<see cref="DockZone.Center"/>, groups only) or beside it
    /// by splitting; every gesture ends here.</summary>
    /// <param name="index">Where among the target's tabs it lands, or -1 for last. Center only.</param>
    /// <param name="size">What the newcomer takes; null means half.</param>
    /// <param name="beside">Land next to the target rather than inside it, so an edge anchor does not divide the document
    /// area.</param>
    public bool MovePane(string paneId, PaneNode target, DockZone zone, int index = -1, PaneLength? size = null,
        bool beside = false)
    {
        if (target == null) return false;

        var source = FindGroup(paneId);
        if (source == null) return false;

        var group = target as PaneGroupNode;
        if (zone is DockZone.Center or DockZone.Floating && group == null) return false;

        // Splitting a group off itself when it holds nothing else: the removal empties the target, and the split is then
        // made against a node no longer in the tree.
        if (ReferenceEquals(source, target) && source.PaneIds.Count == 1 && zone is not (DockZone.Center or DockZone.Floating)) return false;

        source.Remove(paneId);

        if (zone is DockZone.Center or DockZone.Floating)
        {
            if (index < 0) group.Add(paneId);
            else group.Insert(index, paneId);

            // A tab dropped into a strip is the one you are now looking at - dropping it behind whatever was showing
            // means carrying something across the screen and having nothing happen.
            group.ActiveIndex = group.PaneIds.IndexOf(paneId);
        }
        else
        {
            var moved = new PaneGroupNode();
            moved.Add(paneId);

            // A drop INTO the document area splits the area itself, so it gets a node of its own - see the
            // nest parameter. Anything else joins the row it lands in, as before.
            var splittingTheWell = !beside && IsWell(target);

            if (BandFor(target, size) is { } band) SplitWithLength(target, zone, moved, band, splittingTheWell);
            else Split(target, zone, moved, nest: splittingTheWell);

            GrowWellAround(splittingTheWell, moved);
        }

        Normalize();
        return true;
    }

    // A drop into the document area splits within it: the new split becomes the well, so the arrival is a document.
    private void GrowWellAround(bool splittingTheWell, PaneNode arrival)
    {
        if (!splittingTheWell) return;
        if (IsDocument(arrival)) return;                     // already inside - the split happened deeper in

        // The area of the WINDOW the drop landed in: a floating window has one of its own.
        if (RootOf(arrival) is not { DocumentWell: { } well } root) return;
        if (well.Parent is { } grown) root.DocumentWell = grown;
    }

    /// <summary>The root a node belongs to, or null.</summary>
    public DockingRoot RootOf(PaneNode node)
    {
        foreach (var root in Roots)
        {
            if (Holds(root.Content, node)) return root;

            foreach (var bar in root.Bars.Values)
            {
                if (node is PaneGroupNode group && bar.Contains(group)) return root;
            }
        }

        return null;
    }

    /// <summary>Every put-away panel of every root - not in any tree, so nothing walking the trees finds them.</summary>
    public IEnumerable<PaneGroupNode> BarredGroups
    {
        get
        {
            foreach (var root in Roots)
            {
                foreach (var bar in root.Bars.Values)
                {
                    foreach (var group in bar) yield return group;
                }
            }
        }
    }

    public bool CollapseGroup(PaneGroupNode group)
    {
        if (group is not { State: PaneGroupState.Docked } || group.IsEmpty) return false;
        if (group.Parent == null) return false;   // the only group in its root
        if (IsDocument(group)) return false;      // a document group folds to nothing: the centre has no edge to fold against

        // A panel in the MIDDLE of a row has no edge to be put away against, and inventing one puts its strip somewhere
        // it never was.
        var edge = EdgeOf(group);
        if (edge == DockZone.None) return false;

        var root = RootOf(group);
        if (root == null) return false;

        // OUT of the tree, INTO the edge's bar: a panel that is not part of the layout is not
        // part of its structure either, so no split, divider or normalise can reach it.
        group.RestoreLength = group.Length;
        group.Length = PaneLength.Auto;
        group.State = PaneGroupState.Collapsed;

        group.Parent?.Children.Remove(group);
        group.Parent = null;
        root.Bars[edge].Add(group);

        Normalize();
        return true;
    }

    /// <summary>Shows a put-away group's body without pinning it; the strip stays on the edge and the body takes its
    /// <see cref="PaneGroupNode.RestoreLength"/>, pushing neighbors aside.</summary>
    public bool RevealGroup(PaneGroupNode group)
    {
        if (group is not { State: PaneGroupState.Collapsed }) return false;

        // The LENGTH does not change: in the tree a revealed panel is still just its strip. Its body is shown OVER the
        // neighbours (a flyout), not by pushing them aside. Giving it back its docked length here is what
        // made it shove the layout about every time anyone glanced at a tool.
        group.State = PaneGroupState.Revealed;
        return true;
    }

    /// <summary>Puts a revealed group's body away again, leaving the strip. The mirror of <see cref="RevealGroup"/>.</summary>
    public bool HideGroup(PaneGroupNode group)
    {
        if (group is not { State: PaneGroupState.Revealed }) return false;

        // Only the state: the length has been Auto throughout, and RestoreLength still holds what the panel is worth
        // docked. Copying the current length into it here would overwrite that with Auto.
        group.State = PaneGroupState.Collapsed;
        return true;
    }

    /// <summary>Which side of the layout a node sits against, or <see cref="DockZone.None"/> when it is not against one:
    /// it is the first or last child of a row, and the row's orientation says which pair of sides that means.
    /// <para>READ from the tree rather than remembered. A group's authored zone says where it STARTED; after a drag or a
    /// drop it may be somewhere else entirely, and a collapsed panel has to fold towards the edge it is actually on.</para>
    /// </summary>
    public static DockZone EdgeOf(PaneNode node)
    {
        if (node?.Parent is not { } row) return DockZone.None;

        var horizontal = row.Orientation == Orientation.Horizontal;
        if (ReferenceEquals(row.Children[0], node)) return horizontal ? DockZone.Left : DockZone.Top;
        if (ReferenceEquals(row.Children[^1], node)) return horizontal ? DockZone.Right : DockZone.Bottom;
        return DockZone.None;
    }

    /// <summary>Pins a folded group back into the layout, giving it the room it had - from either folded state, since
    /// pinning is what a revealed panel is waiting for. Its place never changed, so there is nowhere to put it back,
    /// only a size to return.</summary>
    public bool ExpandGroup(PaneGroupNode group)
    {
        if (group == null || group.State == PaneGroupState.Docked) return false;

        var root = RootOf(group);
        var edge = root?.EdgeOfBarred(group) ?? DockZone.None;
        if (root == null || edge == DockZone.None) return false;

        root.Bars[edge].Remove(group);

        group.Length = group.RestoreLength;
        group.State = PaneGroupState.Docked;

        // Back into the tree against the SAME edge it was put away on. Its exact former slot is not remembered on
        // purpose: the tree has been free to change while it was away, and a slot restored into a layout that no longer
        // has it would be a guess. An edge is a thing that always still exists.
        if (root.Content == null)
        {
            root.Content = group;
        }
        else
        {
            var target = edge is DockZone.Top or DockZone.Bottom ? BandTarget(root) : root.Content;
            SplitWithLength(target, edge, group, group.RestoreLength);
        }

        Normalize();
        return true;
    }

    /// <summary>Removes a pane wherever it is. Empty groups and the levels they leave behind go with it.</summary>
    public bool RemovePane(string paneId)
    {
        foreach (var root in Roots)
        {
            if (!RemoveFrom(root.Content, paneId)) continue;
            Normalize();
            return true;
        }

        // And out of a put-away panel, which is in no tree at all. A panel emptied this way leaves its bar with it -
        // an edge strip with no tabs on it is nothing anybody can see or reach.
        foreach (var root in Roots)
        {
            foreach (var bar in root.Bars.Values)
            {
                for (var i = bar.Count - 1; i >= 0; i--)
                {
                    if (!bar[i].Remove(paneId)) continue;

                    if (bar[i].IsEmpty) bar.RemoveAt(i);
                    Normalize();
                    return true;
                }
            }
        }

        return false;
    }

    private static bool RemoveFrom(PaneNode node, string paneId)
    {
        switch (node)
        {
            case PaneGroupNode group:
                return group.Remove(paneId);
            case PaneSplitNode split:
                foreach (var child in split.Children)
                {
                    if (RemoveFrom(child, paneId)) return true;
                }
                return false;
            default:
                return false;
        }
    }

    /// <summary>Takes a whole group, panes and selection intact, into a root of its own; refused for a group that already is a
    /// root's whole content.</summary>
    public DockingRoot TearOffGroup(PaneGroupNode group)
    {
        if (group is not { IsEmpty: false }) return null;

        // A PUT-AWAY panel travels too, and it comes out of its edge's bar rather than out of the tree - it has not
        // been in the tree since it was put away.
        var from = RootOf(group);

        // The MAIN window's document area is a PLACE, and a place may be emptied to nothing: its LAST group leaves like
        // any other and an empty one takes its spot, so the centre is still there to open a document into. Holding the
        // last one back instead meant a panel just opened in the centre could not be carried out again.
        // A FLOATING window keeps no such place - emptied, it is a window of nothing and closes.
        var keepsThePlace = from is { IsMain: true } && IsLastWellGroup(group);

        var barred = from?.EdgeOfBarred(group) ?? DockZone.None;

        if (barred != DockZone.None)
        {
            from.Bars[barred].Remove(group);
        }
        else if (group.Parent == null && !keepsThePlace)
        {
            return null;   // a whole root already: a floating panel cannot be torn off itself
        }

        if (keepsThePlace)
        {
            var emptied = new PaneGroupNode { Length = group.Length };
            var wasTheWell = IsWell(group);
            TakeThePlaceOf(group, emptied);
            if (wasTheWell) from.DocumentWell = emptied;
        }

        group.Parent?.Children.Remove(group);
        group.Parent = null;

        // Folded state belongs to the edge it was folded against, and it has just left that edge.
        group.State = PaneGroupState.Docked;
        group.Length = group.RestoreLength;

        // What was carried in becomes the new window's document well, so a drop means the same in every window.
        var root = new DockingRoot(group, isMain: false) { DocumentWell = group };
        Roots.Add(root);

        Normalize();
        return root;
    }

    // Puts one node exactly where another stands - inside its row, or as a root's whole content.
    private void TakeThePlaceOf(PaneNode leaving, PaneNode arriving)
    {
        if (leaving.Parent is { } parent)
        {
            parent.Replace(leaving, arriving);
            return;
        }

        foreach (var root in Roots)
        {
            if (ReferenceEquals(root.Content, leaving)) root.Content = arriving;
        }
    }

    /// <summary>Moves a whole node, as when a floating window is dropped on the compass: <see cref="DockZone.Center"/> tabs its
    /// panes into the target group, other zones split beside it.</summary>
    /// <param name="beside">Land NEXT TO the target rather than inside it - see <see cref="MovePane"/>.</param>
    public bool MoveNode(PaneNode node, PaneNode target, DockZone zone, PaneLength? size = null, bool beside = false)
    {
        if (node == null || target == null || ReferenceEquals(node, target)) return false;
        // The MAIN window's area stays put: things move around it and inside it, never it. A FLOATING
        // window's area is a window - docking it back is exactly what it is for, and refusing that left a torn-off
        // editor that had been split unable to return at all.
        if (IsWell(node) && RootOf(node) is { IsMain: true }) return false;

        // Into itself: the target would leave the tree along with the node, and the split would then be made against
        // something nothing points at any more.
        if (IsWithin(target, node)) return false;

        // Only a GROUP can be tabbed into. What is tabbed IN may be a whole split - a window holding two panels side by
        // side, dropped on a centre indicator, puts every pane it holds into that strip. Refusing it meant a window that
        // had been split could never be docked back whole, only a tab at a time, and nothing said why.
        var tabbed = zone is DockZone.Center or DockZone.Floating;
        if (tabbed && target is not PaneGroupNode) return false;

        // Everything that could refuse has refused; only now is the node taken out of where it is.
        if (node.Parent is { } row)
        {
            row.Children.Remove(node);
        }
        else
        {
            // A whole root - the floating window case. The main one is not something that can be docked into another.
            var owner = Roots.FirstOrDefault(r => ReferenceEquals(r.Content, node));
            if (owner == null || owner.IsMain) return false;
            Roots.Remove(owner);
        }

        node.Parent = null;

        // A fold describes the edge the group was folded against, and it has just left that edge.
        if (node is PaneGroupNode group)
        {
            group.State = PaneGroupState.Docked;
            group.Length = group.RestoreLength;
        }

        var splittingTheWell = !tabbed && !beside && IsWell(target);

        if (tabbed)
        {
            var into = (PaneGroupNode)target;

            // Which pane the arrival was SHOWING - that is what stays in front once it lands. Read before the move: for
            // a whole split it is the first pane in it, and afterwards the node is empty.
            var showing = node is PaneGroupNode arriving && !arriving.IsEmpty
                ? arriving.PaneIds[Math.Clamp(arriving.ActiveIndex, 0, arriving.PaneIds.Count - 1)]
                : PanesIn(node).FirstOrDefault();

            foreach (var pane in PanesIn(node)) into.Add(pane);

            // What was just carried in is what you are looking at (see MovePane).
            if (showing != null) into.ActiveIndex = into.PaneIds.IndexOf(showing);
        }
        else if (BandFor(target, size) is { } band)
        {
            SplitWithLength(target, zone, node, band, splittingTheWell);
        }
        else
        {
            Split(target, zone, node, nest: splittingTheWell);
        }

        GrowWellAround(splittingTheWell, node);

        Normalize();
        return true;
    }

    /// <summary>Whether <paramref name="node"/> is <paramref name="ancestor"/> or sits somewhere under it.</summary>
    private static bool IsWithin(PaneNode node, PaneNode ancestor)
    {
        for (var n = node; n != null; n = n.Parent)
        {
            if (ReferenceEquals(n, ancestor)) return true;
        }
        return false;
    }

    /// <summary>What a top or bottom band splits: the center column, never the root, so side panels keep the full height (as
    /// <see cref="FromZones"/> builds it).</summary>
    public PaneNode BandTarget(DockingRoot root)
    {
        if (root?.Content is not PaneSplitNode split || split.Orientation != Orientation.Horizontal) return root?.Content;

        // The branch holding the documents IS the centre column - the sides are its siblings. THIS window's documents:
        // a floating one has an area of its own, and asking the main window about it aimed every band at the whole root.
        foreach (var child in split.Children)
        {
            if (Holds(child, root.DocumentWell)) return child;
        }

        return split;
    }

    private static bool Holds(PaneNode node, PaneNode wanted)
    {
        if (wanted == null) return false;
        if (ReferenceEquals(node, wanted)) return true;

        if (node is not PaneSplitNode split) return false;

        foreach (var child in split.Children)
        {
            if (Holds(child, wanted)) return true;
        }

        return false;
    }

    /// <summary>Every pane under a node, in tree order. What is being dragged may be one pane, a panel, or a whole split
    /// built up inside a floating window, and several questions - what it is allowed to do, what to call its window -
    /// have to be asked of all of them.</summary>
    /// <summary>Every group in a subtree, in tree order.</summary>
    public static IEnumerable<PaneGroupNode> GroupsIn(PaneNode node)
    {
        switch (node)
        {
            case PaneGroupNode group:
                yield return group;
                break;

            case PaneSplitNode split:
                foreach (var child in split.Children)
                {
                    foreach (var found in GroupsIn(child)) yield return found;
                }
                break;
        }
    }

    /// <summary>The tool panel already standing against an edge, if there is one. What "put this on the left" means once
    /// the left is taken: another TAB in that panel, not a second column beside it - which is what every editor does
    /// with its tool windows, and the only reading that does not let code stack columns until nothing is readable.
    /// A second column is a thing you ask for by DRAGGING, where you can see what it costs.</summary>
    public PaneGroupNode GroupAt(DockingRoot root, DockZone edge)
    {
        if (root?.Content == null || edge is DockZone.None or DockZone.Center or DockZone.Floating) return null;

        foreach (var group in GroupsIn(root.Content))
        {
            if (IsDocument(group)) continue;
            if (EdgeOf(group) == edge) return group;
        }

        // The PUT-AWAY panels of that edge count too - they are still the panel on that side, they are just folded
        // down to their strip, and they live in the edge's bar rather than in the tree. Looking only in the
        // tree meant that folding the top panel away made "open this at the top" build a second one.
        foreach (var group in root.Bars[edge])
        {
            if (!group.IsEmpty) return group;
        }

        return null;
    }

    public static IEnumerable<string> PanesIn(PaneNode node)
    {
        switch (node)
        {
            case PaneGroupNode group:
                foreach (var pane in group.PaneIds) yield return pane;
                break;

            case PaneSplitNode split:
                foreach (var child in split.Children)
                {
                    foreach (var pane in PanesIn(child)) yield return pane;
                }
                break;
        }
    }

    /// <summary>Finds the group holding a pane, or null.</summary>
    public PaneGroupNode FindGroup(string paneId)
    {
        foreach (var root in Roots)
        {
            var found = FindIn(root.Content, paneId);
            if (found != null) return found;
        }

        // The bars too: a put-away panel is not in any tree, but its panes are as findable as anyone's -
        // closing one, navigating to one or dragging one out all start by asking where it is.
        foreach (var group in BarredGroups)
        {
            if (group.PaneIds.Contains(paneId)) return group;
        }

        return null;
    }

    private static PaneGroupNode FindIn(PaneNode node, string paneId)
    {
        switch (node)
        {
            case PaneGroupNode group:
                return group.PaneIds.Contains(paneId) ? group : null;
            case PaneSplitNode split:
                foreach (var child in split.Children)
                {
                    var found = FindIn(child, paneId);
                    if (found != null) return found;
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>Tidies every root: drops empty groups, collapses one-child splits, flattens same-direction nesting, rescales
    /// shares to 1, and removes empty roots except the main one.</summary>
    public void Normalize()
    {
        for (var i = Roots.Count - 1; i >= 0; i--)
        {
            var root = Roots[i];
            root.Content = NormalizeNode(root, root.Content);
            if (root.Content == null && !root.IsMain) Roots.RemoveAt(i);
        }

        // No "unfold the ones that lost their edge" pass any more: put-away panels are not in these trees at all, so
        // nothing that happens here can take an edge away from them.
    }

    private static PaneNode NormalizeNode(DockingRoot root, PaneNode node)
    {
        switch (node)
        {
            case PaneGroupNode group:
                // The well survives being emptied - closing the last document must not take the centre of the layout with
                // it, or the next document opens wherever it likes and the editing area moves under the user.
                if (!group.IsEmpty || IsLastWellGroup(root, group)) return group;

                // Dropped means dropped: a node that is out of the tree must not go on pointing at a live parent, or
                // anything that reads its position - EdgeOf, MoveNode - gets a confident answer about where it is.
                group.Parent = null;
                return null;

            case PaneSplitNode split:
            {
                var kept = new List<PaneNode>();
                foreach (var child in split.Children)
                {
                    var normalized = NormalizeNode(root, child);
                    if (normalized == null) continue;

                    // Same orientation -> take its children as our own instead of keeping the level. The DOCUMENT AREA
                    // is the exception: it is a place, not just a level, and flattening it would spill its groups into
                    // the row beside the tools - which is the same thing as the area swallowing them.
                    if (normalized is PaneSplitNode inner && inner.Orientation == split.Orientation
                        && !ReferenceEquals(inner, root?.DocumentWell))
                    {
                        // Flattened INTO this row, so each grandchild's share of the inner row becomes its share of this
                        // one. Only weights compose that way; a fixed length is already an answer in pixels and stays
                        // exactly what it was.
                        foreach (var grandChild in inner.Children)
                        {
                            if (grandChild.Length.IsStar && normalized.Length.IsStar)
                                grandChild.Length = PaneLength.Stars(grandChild.Length.Value * normalized.Length.Value);
                            kept.Add(grandChild);
                        }
                        continue;
                    }

                    kept.Add(normalized);
                }

                split.Children.Clear();
                foreach (var child in kept) split.Add(child);

                if (split.Children.Count == 0)
                {
                    split.Parent = null;
                    return null;
                }
                if (split.Children.Count == 1)
                {
                    var only = split.Children[0];
                    only.Length = split.Length;   // the survivor stands in the space the split held
                    only.Parent = split.Parent;

                    // The DOCUMENT AREA is a place: closing one of two editors leaves the other as the area, rather
                    // than leaving the area pointing at a node that is no longer in the tree.
                    if (root != null && ReferenceEquals(split, root.DocumentWell)) root.DocumentWell = only;

                    return only;
                }

                split.NormalizeLengths();
                return split;
            }

            default:
                return node;
        }
    }
}
