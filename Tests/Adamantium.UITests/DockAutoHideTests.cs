using System.Linq;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Panels;
using NUnit.Framework;

namespace Adamantium.UITests;

// Collapsing folds a group in place to its tab strip, its length becoming Auto; clicking a tab restores it.
[TestFixture]
public class DockAutoHideTests
{
    private static PaneGroupNode Group(params string[] panes)
    {
        var group = new PaneGroupNode();
        foreach (var pane in panes) group.Add(pane);
        return group;
    }

    /// <summary>The editor shape: documents in the center, an inspector docked right.</summary>
    private static (DockingLayout Layout, PaneGroupNode Documents, PaneGroupNode Inspector) Editor()
    {
        var documents = Group("scene", "game");
        var inspector = Group("inspector");
        var layout = new DockingLayout();
        layout.Roots.Add(new DockingRoot(documents, isMain: true));
        layout.Split(documents, DockZone.Right, inspector, 0.25);
        inspector.Length = PaneLength.Pixels(240);
        return (layout, documents, inspector);
    }

    [Test]
    public void CollapsingAGroup_MovesItOutOfTheTreeAndOntoItsEdge()
    {
        var (layout, _, inspector) = Editor();

        Assert.That(layout.CollapseGroup(inspector), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(inspector.State, Is.EqualTo(PaneGroupState.Collapsed));
            Assert.That(inspector.Parent, Is.Null, "out of the split tree entirely");
            Assert.That(layout.Main.EdgeOfBarred(inspector), Is.EqualTo(DockZone.Right), "and onto the edge it was on");
            Assert.That(inspector.PaneIds, Is.EqualTo(new[] { "inspector" }), "with its panes still in it");
            Assert.That(inspector.RestoreLength, Is.EqualTo(PaneLength.Pixels(240)), "and what it is worth docked");
        });
    }

    [Test]
    public void ACollapsedGroup_RemembersTheRoomItHad()
    {
        var (layout, _, inspector) = Editor();

        layout.CollapseGroup(inspector);

        Assert.That(inspector.RestoreLength, Is.EqualTo(PaneLength.Pixels(240)));
    }

    [Test]
    public void ExpandingAGroup_GivesBackTheRoomItHad()
    {
        var (layout, _, inspector) = Editor();
        layout.CollapseGroup(inspector);

        Assert.That(layout.ExpandGroup(inspector), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(inspector.State, Is.EqualTo(PaneGroupState.Docked));
            Assert.That(inspector.Length, Is.EqualTo(PaneLength.Pixels(240)));
        });
    }

    /// <summary>Collapsing leaves the rest of the layout alone - it is one group giving up room, not a rearrangement.
    /// The stars around it absorb what it released, and a fixed neighbor does not move at all.</summary>
    [Test]
    public void CollapsingAGroup_DoesNotDisturbItsNeighbors()
    {
        var documents = Group("scene");
        var layout = new DockingLayout();
        layout.Roots.Add(new DockingRoot(documents, isMain: true));
        layout.Split(documents, DockZone.Right, Group("inspector"), 0.25);
        var console = Group("console");
        layout.Split(documents, DockZone.Bottom, console, 0.25);

        var documentsLength = documents.Length;

        layout.CollapseGroup(console);

        Assert.That(documents.Length, Is.EqualTo(documentsLength), "what the documents were worth they are still worth");
    }

    /// <summary>
    /// A collapsed group stays collapsed through everything else that happens to the layout. Measured: any operation
    /// that normalized the tree - a tear-off, a drop, a close - turned Auto back into a star and the panel sprang open,
    /// because Auto carries no number and "no number" was being read as "nobody set a weight".
    /// </summary>
    [Test]
    public void ACollapsedGroup_SurvivesEverythingElseHappeningToTheLayout()
    {
        var documents = Group("scene", "game");
        var layout = new DockingLayout();
        layout.Roots.Add(new DockingRoot(documents, isMain: true));
        var console = Group("console");
        layout.Split(documents, DockZone.Bottom, console, 0.25);

        layout.CollapseGroup(console);

        layout.RemovePane("game");   // the tear-off, as far as the model is concerned

        Assert.Multiple(() =>
        {
            Assert.That(console.State, Is.EqualTo(PaneGroupState.Collapsed), "still folded down");
            Assert.That(console.Length, Is.EqualTo(PaneLength.Auto), "and still taking only what its strip needs");
        });
    }

    /// <summary>Collapsing the only group in a root is refused: it would leave a window of nothing but tab strips.</summary>
    [Test]
    public void CollapsingTheOnlyGroup_IsRefused()
    {
        var only = Group("scene");
        var layout = new DockingLayout();
        layout.Roots.Add(new DockingRoot(only, isMain: true));

        Assert.Multiple(() =>
        {
            Assert.That(layout.CollapseGroup(only), Is.False);
            Assert.That(only.State, Is.EqualTo(PaneGroupState.Docked));
        });
    }

    /// <summary>A panel that is not ON an edge cannot be put away: folding is a statement about an edge, and it has none.
    /// <para>Measured before the rule: it folded where it stood - caption and body gone, strip still horizontal because
    /// there was no side to turn towards - and what was left was a wide empty box with tabs along the bottom.</para>
    /// </summary>
    [Test]
    public void APanelInTheMiddleOfARow_CannotBePutAway()
    {
        var (layout, _, inspector) = Editor();

        // Something docked OUTSIDE it: the inspector is now between the documents and the newcomer.
        layout.MovePane("game", layout.Main.Content, DockZone.Right, size: PaneLength.Pixels(240));

        Assert.Multiple(() =>
        {
            Assert.That(DockingLayout.EdgeOf(inspector), Is.EqualTo(DockZone.None), "not on an edge");
            Assert.That(layout.CollapseGroup(inspector), Is.False, "so it cannot fold");
            Assert.That(inspector.State, Is.EqualTo(PaneGroupState.Docked));
        });
    }

    /// <summary>Collapsing twice, or expanding what is not collapsed, changes nothing - so the remembered size cannot be
    /// overwritten with Auto by a second press.</summary>
    [Test]
    public void CollapseAndExpand_AreIdempotent()
    {
        var (layout, _, inspector) = Editor();

        layout.CollapseGroup(inspector);

        Assert.Multiple(() =>
        {
            Assert.That(layout.CollapseGroup(inspector), Is.False, "already collapsed");
            Assert.That(inspector.RestoreLength, Is.EqualTo(PaneLength.Pixels(240)), "and its remembered size is intact");
        });

        layout.ExpandGroup(inspector);
        Assert.That(layout.ExpandGroup(inspector), Is.False, "already expanded");
    }

    /// <summary>Clicking a tab on a put-away strip REVEALS the panel - it does not pin it back. That is the third state,
    /// and the reason this is an enum: the body comes into view while the panel stays unpinned, so its tabs stay on the
    /// edge until someone actually pins it.</summary>
    [Test]
    public void RevealingAGroup_ShowsItWithoutPinningItBack()
    {
        var (layout, _, inspector) = Editor();
        layout.CollapseGroup(inspector);

        Assert.That(layout.RevealGroup(inspector), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(inspector.State, Is.EqualTo(PaneGroupState.Revealed), "showing, but still not docked");
            Assert.That(inspector.Length, Is.EqualTo(PaneLength.Auto),
                "in the TREE it is still just its strip - the body is a flyout over the neighbors, not a share of the row");
            Assert.That(inspector.RestoreLength, Is.EqualTo(PaneLength.Pixels(240)),
                "and what it is worth docked is remembered - that is how wide the flyout opens");
        });
    }

    /// <summary>Glancing at a tool does not move the layout about. Before the fix, revealing gave the panel
    /// its docked length back, so every look at a tool shoved its neighbors aside and then back again.</summary>
    [Test]
    public void RevealingAGroup_DoesNotDisturbItsNeighbors()
    {
        var (layout, documents, inspector) = Editor();
        layout.CollapseGroup(inspector);
        var documentsLength = documents.Length;

        layout.RevealGroup(inspector);

        Assert.That(documents.Length, Is.EqualTo(documentsLength));
    }

    /// <summary>And putting it away again leaves the strip, with the room it was just given remembered.</summary>
    [Test]
    public void HidingARevealedGroup_LeavesTheStrip()
    {
        var (layout, _, inspector) = Editor();
        layout.CollapseGroup(inspector);
        layout.RevealGroup(inspector);

        Assert.That(layout.HideGroup(inspector), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(inspector.State, Is.EqualTo(PaneGroupState.Collapsed));
            Assert.That(inspector.Length, Is.EqualTo(PaneLength.Auto), "the strip and nothing more");
            Assert.That(inspector.RestoreLength, Is.EqualTo(PaneLength.Pixels(240)));
        });
    }

    /// <summary>Pinning is what docks it, from EITHER folded state - a revealed panel is one press from being kept open,
    /// and that press is the pin.</summary>
    [Test]
    public void PinningARevealedGroup_DocksIt()
    {
        var (layout, _, inspector) = Editor();
        layout.CollapseGroup(inspector);
        layout.RevealGroup(inspector);

        Assert.That(layout.ExpandGroup(inspector), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(inspector.State, Is.EqualTo(PaneGroupState.Docked));
            Assert.That(inspector.Length, Is.EqualTo(PaneLength.Pixels(240)));
        });
    }

    /// <summary>Dragging a tool panel by its CAPTION takes the whole group out, panes and order intact - it is the panel
    /// that is being moved, not one of its tabs.</summary>
    [Test]
    public void TearingOffAGroup_MovesItWholeIntoItsOwnRoot()
    {
        var (layout, documents, inspector) = Editor();
        inspector.Add("hierarchy");

        var root = layout.TearOffGroup(inspector);

        Assert.Multiple(() =>
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.IsMain, Is.False, "a floating root of its own");
            Assert.That(root.Content, Is.SameAs(inspector), "the group itself moved, not a copy");
            Assert.That(inspector.PaneIds, Is.EqualTo(new[] { "inspector", "hierarchy" }), "with every pane, in order");
            Assert.That(layout.Main.Content, Is.SameAs(documents), "and what is left collapses back to the documents");
        });
    }

    /// <summary>An UNPINNED panel torn off comes back DOCKED: the fold described an edge it no longer sits against. Both
    /// folded states travel - a revealed panel is dragged by the caption of its flyout, which is the only caption it has
    /// on screen.</summary>
    [Test]
    public void TearingOffAnUnpinnedGroup_UnfoldsIt()
    {
        var (layout, _, inspector) = Editor();
        layout.CollapseGroup(inspector);
        layout.RevealGroup(inspector);

        Assert.That(layout.TearOffGroup(inspector), Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(inspector.State, Is.EqualTo(PaneGroupState.Docked));
            Assert.That(inspector.Length, Is.EqualTo(PaneLength.Pixels(240)), "with the room it had before it folded");
        });
    }

    /// <summary>A group that is already a whole root is a floating panel - there is nothing to tear it off.</summary>
    [Test]
    public void TearingOffAGroupThatIsAlreadyARoot_DoesNothing()
    {
        var (layout, _, inspector) = Editor();
        var root = layout.TearOffGroup(inspector);

        Assert.That(layout.TearOffGroup((PaneGroupNode)root.Content), Is.Null);
    }

    /// <summary>A floating panel dropped on the compass docks WHOLE: the window was holding a panel, and a panel is what
    /// lands - not the one tab that happened to be showing.</summary>
    [Test]
    public void DroppingAFloatingPanel_DocksTheWholeGroupWhereItWasAimed()
    {
        var (layout, documents, inspector) = Editor();
        inspector.Add("hierarchy");
        var root = layout.TearOffGroup(inspector);

        Assert.That(layout.MoveNode(root.Content, documents, DockZone.Bottom), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Roots, Has.Count.EqualTo(1), "the window's root went with it");
            Assert.That(layout.FindGroup("hierarchy"), Is.SameAs(inspector), "with every pane it held");
            Assert.That(DockingLayout.EdgeOf(inspector), Is.EqualTo(DockZone.Bottom));
        });
    }

    /// <summary>Aimed at the CENTER of a group, its panes become tabs of that group - which is what makes a floating
    /// window somewhere other panels can be collected.</summary>
    [Test]
    public void DroppingAFloatingPanelOntoAGroup_TabsItsPanesIn()
    {
        var (layout, documents, inspector) = Editor();
        inspector.Add("hierarchy");
        var root = layout.TearOffGroup(inspector);

        Assert.That(layout.MoveNode(root.Content, documents, DockZone.Center), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(documents.PaneIds, Is.EqualTo(new[] { "scene", "game", "inspector", "hierarchy" }));
            Assert.That(layout.Roots, Has.Count.EqualTo(1));
        });
    }

    /// <summary>A tab dragged into a FLOATING window lands there like anywhere else - the model has roots, not windows,
    /// so this is the same move as any other and not a second kind.</summary>
    [Test]
    public void APaneCanBeMovedIntoAFloatingRoot()
    {
        var (layout, documents, inspector) = Editor();
        var root = layout.TearOffGroup(inspector);

        Assert.That(layout.MovePane("game", root.Content, DockZone.Center), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(inspector.PaneIds, Is.EqualTo(new[] { "inspector", "game" }));
            Assert.That(documents.PaneIds, Is.EqualTo(new[] { "scene" }));
        });
    }

    /// <summary>The MAIN window's whole content is not something that can be docked into another window - dragging the
    /// main window is moving the window.</summary>
    [Test]
    public void MovingTheMainRootsWholeContent_IsRefused()
    {
        var only = Group("scene");
        var layout = new DockingLayout();
        layout.Roots.Add(new DockingRoot(only, isMain: true));
        var floating = Group("inspector");
        layout.Roots.Add(new DockingRoot(floating, isMain: false));

        Assert.Multiple(() =>
        {
            Assert.That(layout.MoveNode(only, floating, DockZone.Right), Is.False);
            Assert.That(layout.Main.Content, Is.SameAs(only), "and it is still there");
        });
    }

    /// <summary>Nothing can be dropped into itself: the target would leave the tree along with what was dropped.</summary>
    [Test]
    public void MovingANodeOntoItself_IsRefused()
    {
        var (layout, _, inspector) = Editor();
        var root = layout.TearOffGroup(inspector);

        Assert.That(layout.MoveNode(root.Content, root.Content, DockZone.Left), Is.False);
    }

    // --- The document well: the center is a PLACE, not a property of the panes in it ---------------------------------
    // Everything inside the well is a document and everything outside it is a tool, which
    // is what makes a tool dropped into the center behave like a document: the zones for tools are the EDGES, and a panel
    // in the center has no edge to fold against.

    /// <summary>An editor built from authored zones: documents in the center, an inspector on the right.</summary>
    private static (DockingLayout Layout, PaneGroupNode Documents, PaneGroupNode Inspector) Authored()
    {
        var documents = Group("scene");
        var inspector = Group("inspector");
        var layout = DockingLayout.FromZones([
            new ZoneDeclaration(DockZone.Center, documents),
            new ZoneDeclaration(DockZone.Right, inspector)
        ]);
        return (layout, documents, inspector);
    }

    [Test]
    public void TheDocumentWell_IsTheFirstGroupDeclared()
    {
        var (layout, documents, _) = Authored();

        Assert.That(layout.DocumentWell, Is.SameAs(documents));
    }

    /// <summary>A TOOL dropped into the center joins the well, and from then on it is in the place where documents live -
    /// which is the whole point: the group it landed in is a document group, so that is how it is dressed.</summary>
    [Test]
    public void AToolDroppedIntoTheCenter_JoinsTheDocumentWell()
    {
        var (layout, documents, _) = Authored();

        Assert.That(layout.MovePane("inspector", documents, DockZone.Center), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(layout.DocumentWell, Is.SameAs(documents), "the well is still the well");
            Assert.That(documents.PaneIds, Is.EqualTo(new[] { "scene", "inspector" }));
        });
    }

    /// <summary>The center cannot be put away or docked somewhere else: there is no edge for it to fold against, and a
    /// window whose center has moved into a tool panel is not a layout state that should be reachable.
    /// <para>Its CONTENTS are another matter - see DocumentAreaTests: the last group may be carried out into a window
    /// of its own, and what stays behind is the empty place.</para></summary>
    [Test]
    public void TheDocumentWell_CannotBeCollapsedOrMoved()
    {
        var (layout, documents, inspector) = Authored();

        Assert.Multiple(() =>
        {
            Assert.That(layout.CollapseGroup(documents), Is.False, "collapse");
            Assert.That(layout.MoveNode(documents, inspector, DockZone.Right), Is.False, "move");
            Assert.That(layout.DocumentWell, Is.SameAs(documents), "and it is still where it was");
        });
    }

    /// <summary>Closing the last document leaves the center as EMPTY SPACE. Letting it be tidied away would mean the
    /// layout loses its center, and the next document opens wherever it likes.</summary>
    [Test]
    public void TheDocumentWell_SurvivesLosingItsLastDocument()
    {
        var (layout, documents, _) = Authored();

        Assert.That(layout.RemovePane("scene"), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(documents.IsEmpty, Is.True, "empty");
            Assert.That(documents.Parent, Is.Not.Null, "but still in the tree");
            Assert.That(layout.DocumentWell, Is.SameAs(documents));
        });
    }

    /// <summary>An ordinary group, by contrast, goes when its last pane does.</summary>
    [Test]
    public void AnOrdinaryGroup_DoesNotSurviveLosingItsLastPane()
    {
        var (layout, documents, inspector) = Authored();

        layout.RemovePane("inspector");

        Assert.Multiple(() =>
        {
            Assert.That(inspector.Parent, Is.Null);
            Assert.That(layout.Main.Content, Is.SameAs(documents), "what is left collapses back to the documents");
        });
    }

    /// <summary>What is being dragged may be one pane, a panel, or a whole split built up inside a floating window - and
    /// the questions asked of it (what it may do, what to call its window) are asked of ALL the panes in it.</summary>
    [Test]
    public void PanesIn_ListsEveryPaneUnderANode_InTreeOrder()
    {
        var (layout, documents, inspector) = Editor();
        inspector.Add("hierarchy");

        Assert.Multiple(() =>
        {
            Assert.That(DockingLayout.PanesIn(inspector), Is.EqualTo(new[] { "inspector", "hierarchy" }));
            Assert.That(DockingLayout.PanesIn(layout.Main.Content),
                Is.EqualTo(new[] { "scene", "game", "inspector", "hierarchy" }), "across the whole split");
            Assert.That(DockingLayout.PanesIn(documents), Is.EqualTo(new[] { "scene", "game" }));
        });
    }

    // A folded panel that is no longer on an edge (something docked outside it) unfolds.
    [Test]
    public void APutAwayPanel_IsUntouchedByAnythingThatHappensInTheTree()
    {
        var (layout, documents, inspector) = Editor();
        layout.CollapseGroup(inspector);

        // Every kind of drop, one after another: beside the documents, on the edge the panel is folded against, and a
        // band across the bottom. None of them is aimed at the panel, and none of them may reach it.
        layout.MovePane("game", documents, DockZone.Bottom);
        layout.MovePane("scene", layout.Main.Content, DockZone.Right, size: PaneLength.Pixels(240));

        Assert.Multiple(() =>
        {
            Assert.That(layout.Main.EdgeOfBarred(inspector), Is.EqualTo(DockZone.Right), "still on its edge");
            Assert.That(inspector.State, Is.EqualTo(PaneGroupState.Collapsed), "still put away");
            Assert.That(inspector.Parent, Is.Null, "and still not part of the tree");
            Assert.That(inspector.RestoreLength, Is.EqualTo(PaneLength.Pixels(240)), "with its docked size intact");
        });
    }

    /// <summary>Opening the same thing twice does not make two of it - what "navigate to an already-open document"
    /// must mean, or every "go to file" leaves a trail of copies.</summary>
    [Test]
    public void AddingAPaneThatIsAlreadyOpen_ActivatesItInstead()
    {
        var (layout, documents, _) = Authored();

        // What DockingArea.AddPane does to the model when the pane is already somewhere.
        Assert.That(layout.FindGroup("scene"), Is.SameAs(documents));

        documents.ActiveIndex = 0;
        layout.RevealGroup(documents);   // refused: it is docked, not folded - nothing to reveal

        Assert.Multiple(() =>
        {
            Assert.That(documents.PaneIds.Count(id => id == "scene"), Is.EqualTo(1), "still one of it");
            Assert.That(documents.State, Is.EqualTo(PaneGroupState.Docked));
        });
    }

    /// <summary>A band dropped on the BOTTOM edge anchor splits the center column, not the whole window - so the side
    /// panels keep their full height and a put-away strip stays on the edge it is folded against.
    /// <para>Measured before the rule: the band was aimed at the root, which cut the right-hand strip off at the band's
    /// top edge - and a strip pushed off its edge is no longer a strip on an edge.</para></summary>
    [Test]
    public void ABandDroppedOnTheBottomAnchor_DoesNotRunUnderTheSides()
    {
        var (layout, documents, inspector) = Authored();
        layout.CollapseGroup(inspector);

        var console = Group("console");
        var target = layout.BandTarget(layout.Main);

        Assert.That(target, Is.SameAs(documents), "the center column is what a band splits");

        layout.Split(target, DockZone.Bottom, console);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Main.EdgeOfBarred(inspector), Is.EqualTo(DockZone.Right), "the side is still on its edge");
            Assert.That(inspector.State, Is.EqualTo(PaneGroupState.Collapsed), "and still put away");
            Assert.That(DockingLayout.EdgeOf(console), Is.EqualTo(DockZone.Bottom));
        });
    }

    /// <summary>Reveal only means anything for a panel that is put away, and hide only for one that is showing.</summary>
    [Test]
    public void RevealAndHide_OnlyApplyToTheStateTheyBelongTo()
    {
        var (layout, _, inspector) = Editor();

        Assert.Multiple(() =>
        {
            Assert.That(layout.RevealGroup(inspector), Is.False, "a docked panel is already showing");
            Assert.That(layout.HideGroup(inspector), Is.False, "and a docked one is not hidden by this");
        });
    }
}
