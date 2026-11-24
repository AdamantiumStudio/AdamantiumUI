using System.Linq;
using Adamantium.UI.WebView.Accessibility;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

/// <summary>
/// The page's accessibility tree read from Chromium's JSON - shaped as CEF sent it for a probe page (measured 24.11):
/// which nodes are shown, where they stand, what happened.
/// </summary>
[TestFixture]
public class WebViewPageAccessibilityTests
{
    // A document holding a nameless box around a heading (its text inside), a paragraph with a link, and a field.
    private const string Page = """
        {"ax_tree_id":"T1","events":[{"event_type":"loadComplete","id":1}],
         "updates":[{"has_tree_data":true,"root_id":1,"tree_data":{"focus_id":1,"title":"Probe"},
          "nodes":[
           {"id":1,"role":"rootWebArea","attributes":{"name":"Probe","scrollY":100},"child_ids":[2],
            "location":{"x":0,"y":0,"width":800,"height":600},"offset_container_id":0,"state":["focusable"]},
           {"id":2,"role":"genericContainer","attributes":{},"child_ids":[3,5,8],"state":["ignored"],
            "location":{"x":0,"y":0,"width":800,"height":900},"offset_container_id":1},
           {"id":3,"role":"heading","attributes":{"name":"Title","nameFrom":"contents","htmlTag":"h1"},"child_ids":[4],
            "location":{"x":10,"y":20,"width":300,"height":40},"offset_container_id":1},
           {"id":4,"role":"staticText","attributes":{"name":"Title"},"child_ids":[],
            "location":{"x":10,"y":20,"width":120,"height":40},"offset_container_id":1},
           {"id":5,"role":"paragraph","attributes":{},"child_ids":[6],
            "location":{"x":10,"y":200,"width":300,"height":20},"offset_container_id":1},
           {"id":6,"role":"link","attributes":{"name":"go there","nameFrom":"contents"},"child_ids":[7],
            "location":{"x":40,"y":200,"width":60,"height":20},"offset_container_id":1,"state":["focusable","linked"]},
           {"id":7,"role":"inlineTextBox","attributes":{},"child_ids":[],
            "location":{"x":0,"y":0,"width":60,"height":20},"offset_container_id":6},
           {"id":8,"role":"textField","attributes":{"name":"Your name","value":"Ann","htmlId":"name","inputType":"text"},
            "child_ids":[9],"location":{"x":10,"y":300,"width":200,"height":30},"offset_container_id":1,
            "state":["editable","focusable"]},
           {"id":9,"role":"genericContainer","attributes":{},"child_ids":[],
            "location":{"x":6,"y":4,"width":180,"height":22},"offset_container_id":8,"state":["editable"]}
          ]}]}
        """;

    private static WebViewPageAccessibility Read()
    {
        var page = new WebViewPageAccessibility();
        page.ApplyTreeChange(System.Text.Encoding.UTF8.GetBytes(Page));
        return page;
    }

    // Ignored boxes give their children up; the heading's own text, the link's text pieces and the field's insides are
    // not shown again.
    [Test]
    public void TheDocument_ShowsWhatAReaderNames_AndNothingTwice()
    {
        var page = Read();
        var root = page.Document.Root;
        var shown = page.ShownChildren(root);

        Assert.Multiple(() =>
        {
            Assert.That(shown.Select(node => node.Role), Is.EqualTo(new[] { "heading", "paragraph", "textField" }));
            Assert.That(page.ShownChildren(shown[0]), Is.Empty, "the heading's text is its name");
            Assert.That(page.ShownChildren(shown[1]).Select(node => node.Name), Is.EqualTo(new[] { "go there" }));
            Assert.That(page.ShownChildren(page.ShownChildren(shown[1])[0]), Is.Empty, "a link's text pieces");
            Assert.That(page.ShownChildren(shown[2]), Is.Empty, "a field's insides");
            Assert.That(shown[2].Value, Is.EqualTo("Ann"));
            Assert.That(shown[2].HtmlId, Is.EqualTo("name"));
        });
    }

    // A node stands where its container does, less what the container scrolled; one inside a field, from the field.
    [Test]
    public void ANode_StandsFromItsContainer_LessItsScroll()
    {
        var page = Read();
        var tree = page.Document;

        Assert.Multiple(() =>
        {
            Assert.That(page.PageRect(tree.Find(3)).Y, Is.EqualTo(20 - 100), "the document scrolled 100 down");
            Assert.That(page.PageRect(tree.Find(9)).X, Is.EqualTo(10 + 6));
            Assert.That(page.PageRect(tree.Find(9)).Y, Is.EqualTo(300 + 4 - 100));
        });
    }

    // Shown under its nearest holder that is shown: the ignored box between is passed over.
    [Test]
    public void ANode_IsShownUnderItsNearestShownHolder()
    {
        var page = Read();
        var tree = page.Document;

        Assert.Multiple(() =>
        {
            Assert.That(page.ShownParent(tree.Find(3)), Is.SameAs(tree.Root));
            Assert.That(page.ShownParent(tree.Find(6)), Is.SameAs(tree.Find(5)));
            Assert.That(page.ShownParent(tree.Root), Is.Null, "the document is the view's");
        });
    }

    // The focus moving in the tree's data is told as a focus event, once; a node no longer named goes.
    [Test]
    public void TheFocusMoving_IsTold_AndANodeTakenOutGoes()
    {
        var page = Read();
        page.ApplyTreeChange("""
            {"ax_tree_id":"T1","updates":[{"tree_data":{"focus_id":8},
              "nodes":[{"id":5,"role":"paragraph","attributes":{},"child_ids":[],
                "location":{"x":10,"y":200,"width":300,"height":20},"offset_container_id":1}]}]}
            """u8);

        Assert.Multiple(() =>
        {
            Assert.That(page.Focused?.Id, Is.EqualTo(8));
            Assert.That(page.Events.Select(told => told.Kind), Is.EqualTo(new[] { "focus" }));
            Assert.That(page.Document.Find(6), Is.Null, "the link was taken out of the paragraph");
            Assert.That(page.Document.Find(7), Is.Null);
        });
    }

    // A new document of the page takes the old one's place.
    [Test]
    public void ANewDocument_TakesTheOldOnesPlace()
    {
        var page = Read();
        page.ApplyTreeChange("""
            {"ax_tree_id":"T2","updates":[{"root_id":1,"tree_data":{"focus_id":1},
              "nodes":[{"id":1,"role":"rootWebArea","attributes":{"name":"Next"},"child_ids":[]}]}]}
            """u8);

        Assert.That(page.Document.Root.Name, Is.EqualTo("Next"));
    }

    // Moved nodes take their new place.
    [Test]
    public void AMovedNode_TakesItsNewPlace()
    {
        var page = Read();
        page.ApplyLocationChange("""
            [{"ax_tree_id":"T1","id":3,"new_location":{"offset_container_id":1,
              "bounds":{"x":10,"y":500,"width":300,"height":40}}}]
            """u8);

        Assert.That(page.PageRect(page.Document.Find(3)).Y, Is.EqualTo(500 - 100));
    }

    // A part of the page sent again whole - cleared, then named with the same children - keeps them.
    [Test]
    public void APartSentAgainWhole_KeepsTheChildrenItNamesAgain()
    {
        var page = Read();
        page.ApplyTreeChange("""
            {"ax_tree_id":"T1","updates":[{"node_id_to_clear":5,
              "nodes":[{"id":5,"role":"paragraph","attributes":{},"child_ids":[6],
                        "location":{"x":10,"y":200,"width":300,"height":20},"offset_container_id":1},
                       {"id":6,"role":"link","attributes":{"name":"go there","nameFrom":"contents"},"child_ids":[],
                        "location":{"x":40,"y":200,"width":60,"height":20},"offset_container_id":1}]}]}
            """u8);

        Assert.Multiple(() =>
        {
            Assert.That(page.Document.Find(6), Is.Not.Null);
            Assert.That(page.ShownChildren(page.Document.Find(5)).Select(node => node.Name), Is.EqualTo(new[] { "go there" }));
        });
    }

    // A late update of a document the page has left is not taken in: it would bring the old document back.
    [Test]
    public void ALateUpdateOfADocumentLeft_IsNotTakenIn()
    {
        var page = Read();
        page.ApplyTreeChange("""
            {"ax_tree_id":"T2","updates":[{"root_id":1,"tree_data":{"focus_id":1},
              "nodes":[{"id":1,"role":"rootWebArea","attributes":{"name":"Next"},"child_ids":[]}]}]}
            """u8);

        var taken = page.ApplyTreeChange("""
            {"ax_tree_id":"T1","updates":[{"tree_data":{"focus_id":8},"nodes":[]}]}
            """u8);

        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.False);
            Assert.That(page.Document.Id, Is.EqualTo("T2"));
        });
    }

    // A frame's tree that came before its page's document stays - Chromium sends it once; taken out of the page, it goes.
    [Test]
    public void AFrameBeforeItsDocument_Stays_AndGoesWithItsHost()
    {
        var page = new WebViewPageAccessibility();
        page.ApplyTreeChange("""
            {"ax_tree_id":"F1","updates":[{"root_id":1,"tree_data":{"focus_id":1,"parent_tree_id":"D1"},
              "nodes":[{"id":1,"role":"rootWebArea","attributes":{"name":"Ad"},"child_ids":[]}]}]}
            """u8);
        page.ApplyTreeChange("""
            {"ax_tree_id":"D1","updates":[{"root_id":1,"tree_data":{"focus_id":1},
              "nodes":[{"id":1,"role":"rootWebArea","attributes":{"name":"Page"},"child_ids":[2]},
                       {"id":2,"role":"iframe","attributes":{"childTreeId":"F1"},"child_ids":[]}]}]}
            """u8);
        var frameShown = page.ShownChildren(page.Document.Find(2)).Select(node => node.Name).ToList();
        var frameNode = page.ShownChildren(page.Document.Find(2))[0];

        page.ApplyTreeChange("""
            {"ax_tree_id":"D1","updates":[{"nodes":[{"id":1,"role":"rootWebArea","attributes":{"name":"Page"},"child_ids":[]}]}]}
            """u8);

        Assert.Multiple(() =>
        {
            Assert.That(frameShown, Is.EqualTo(new[] { "Ad" }));
            Assert.That(page.Holds(frameNode), Is.False, "the frame went with the node showing it");
        });
    }
}
