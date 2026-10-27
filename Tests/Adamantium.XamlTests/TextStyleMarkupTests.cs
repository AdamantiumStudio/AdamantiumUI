using Adamantium.Core.Fonts;
using Adamantium.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Paragraph and character styles written in markup as resources and set on paragraphs and runs: the build
/// compiles them, the designer's loader builds the same.</summary>
[TestFixture]
public class TextStyleMarkupTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private const string Panel =
        "<StackPanel " + Namespaces + "><StackPanel.Resources>" +
        "<ParagraphStyle x:Key=\"Body\" FontSize=\"16\" Margin=\"0,0,0,8\"/>" +
        "<ParagraphStyle x:Key=\"Heading\" FontSize=\"28\" TextIndent=\"0\"/>" +
        "<CharacterStyle x:Key=\"Emphasis\" FontWeight=\"Bold\"/>" +
        "</StackPanel.Resources>" +
        "<TextBlock><Paragraph ParagraphStyle=\"{ResourceReference Heading}\">Title</Paragraph>" +
        "<Paragraph ParagraphStyle=\"{ResourceReference Body}\">Body <Run CharacterStyle=\"{ResourceReference Emphasis}\" Text=\"em\"/></Paragraph>" +
        "</TextBlock></StackPanel>";

    [Test]
    public void Styles_Compile()
    {
        var errors = AumlCodegenHarness.Compile(
            "<Window " + Namespaces + ">" + Panel.Replace(" " + Namespaces, string.Empty) + "</Window>");

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
    }

    // The designer's loader takes no static resources; the styles written in place.
    private const string Block =
        "<TextBlock " + Namespaces + "><Paragraph><Paragraph.ParagraphStyle>" +
        "<ParagraphStyle FontSize=\"28\" Margin=\"0,0,0,8\" TextIndent=\"0\"><ParagraphStyle.BasedOn>" +
        "<ParagraphStyle FontSize=\"16\"/></ParagraphStyle.BasedOn></ParagraphStyle>" +
        "</Paragraph.ParagraphStyle>Title</Paragraph>" +
        "<Paragraph>Body <Run Text=\"em\"><Run.CharacterStyle><CharacterStyle FontWeight=\"Bold\"/></Run.CharacterStyle></Run>" +
        "</Paragraph></TextBlock>";

    [Test]
    public void TheDesignersLoader_BuildsTheStyles()
    {
        var result = AumlLoader.Load(Block);

        Assert.That(result.Diagnostics, Is.Empty, string.Join(" | ", result.Diagnostics));
        var text = (TextBlock)result.Root;
        var heading = ((Paragraph)text.Inlines[0]).ParagraphStyle;
        var body = (Paragraph)text.Inlines[1];
        Assert.Multiple(() =>
        {
            Assert.That(heading.FontSize, Is.EqualTo(28));
            Assert.That(heading.BasedOn.FontSize, Is.EqualTo(16));
            Assert.That(heading.Margin, Is.EqualTo(new Thickness(0, 0, 0, 8)));
            Assert.That(((Run)body.Inlines[1]).CharacterStyle.FontWeight, Is.EqualTo(FontWeight.Bold));
        });
    }
}

/// <summary>A style a paragraph references from an element's resources, as the built markup sets it: deferred, and
/// answered from its block's place in the tree.</summary>
[TestFixture]
public class TextStyleResourceTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new Adamantium.Core.DependencyInjection.AdamantiumDependencyContainer());
        Adamantium.UI.Core.UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void FreshResources()
    {
        _app.ResourceManager = new Adamantium.UI.Core.Resources.ResourceManager();
        typeof(Adamantium.UI.Core.UIAppContext).GetProperty(nameof(Adamantium.UI.Core.UIAppContext.Current))
            .SetValue(null, _app);
    }

    [Test]
    public void AStyleReferencedFromAnElementsResources_IsTheOneTheParagraphTakes()
    {
        var heading = new ParagraphStyle { FontSize = 28 };
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = "Title" });
        Adamantium.UI.Core.Resources.ResourceResolver.SetDeferred(paragraph, nameof(Paragraph.ParagraphStyle), "Heading");
        var text = new TextBlock();
        text.Inlines.Add(paragraph);
        var panel = new StackPanel { Children = { text } };
        var resources = new Adamantium.UI.Core.Resources.ResourceDictionary();
        resources.Add("Heading", heading);
        Adamantium.UI.Core.Resources.ResourceContext.SetResources(panel, resources);
        var window = new Adamantium.UI.Controls.Window { Width = 400, Height = 300, Content = panel };
        for (var i = 0; i < 5; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }

        Assert.That(paragraph.ParagraphStyle, Is.SameAs(heading));
    }

    [Test]
    public void AScopeSwitched_ReachesTheInlinesAndTheirStyles()
    {
        var run = new Run { Text = "x" };
        Adamantium.UI.Core.Resources.ResourceResolver.SetDeferred(run, nameof(Run.CharacterStyle), "Emphasis");
        var style = new CharacterStyle();
        Adamantium.UI.Core.Resources.ResourceResolver.SetDeferred(style, nameof(CharacterStyle.FontSize), "Size");
        var text = new TextBlock();
        text.Inlines.Add(new Run { Text = "y", CharacterStyle = style });
        text.Inlines.Add(new Span { Inlines = { run } });
        var panel = new StackPanel { Children = { text } };
        var resources = new Adamantium.UI.Core.Resources.ResourceDictionary();
        resources.Add("Emphasis", new CharacterStyle());
        resources.Add("Size", 12.0);
        Adamantium.UI.Core.Resources.ResourceContext.SetResources(panel, resources);
        var window = new Adamantium.UI.Controls.Window { Width = 400, Height = 300, Content = panel };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        var switched = new CharacterStyle();
        resources["Emphasis"] = switched;
        resources["Size"] = 30.0;

        Adamantium.UI.Core.Resources.ResourceResolver.ReResolveSubtree(panel);

        Assert.Multiple(() =>
        {
            Assert.That(run.CharacterStyle, Is.SameAs(switched));
            Assert.That(style.ResolvedFontSize(text), Is.EqualTo(30), "as the block sees it");
            Assert.That(style.FontSize, Is.NaN, "the shared style keeps no answer");
        });
    }

    // A style shared by blocks in two scopes answered from the one that resolved it last, for both.
    [Test]
    public void AStyleSharedByTwoScopes_GivesEachBlockItsOwnAnswer()
    {
        var style = new CharacterStyle();
        Adamantium.UI.Core.Resources.ResourceResolver.SetDeferred(style, nameof(CharacterStyle.FontSize), "Size");
        TextBlock InScope(double size, out StackPanel panel)
        {
            var text = new TextBlock();
            text.Inlines.Add(new Run { Text = "Shared", CharacterStyle = style });
            panel = new StackPanel { Children = { text } };
            var resources = new Adamantium.UI.Core.Resources.ResourceDictionary();
            resources.Add("Size", size);
            Adamantium.UI.Core.Resources.ResourceContext.SetResources(panel, resources);
            return text;
        }

        var small = InScope(12, out var smallPanel);
        var large = InScope(30, out var largePanel);
        var window = new Adamantium.UI.Controls.Window
        {
            Width = 400, Height = 300, Content = new StackPanel { Children = { smallPanel, largePanel } },
        };
        for (var i = 0; i < 5; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        }

        Assert.Multiple(() =>
        {
            Assert.That(style.ResolvedFontSize(small), Is.EqualTo(12));
            Assert.That(style.ResolvedFontSize(large), Is.EqualTo(30));
            Assert.That(small.DesiredSize.Height, Is.LessThan(large.DesiredSize.Height), "each laid out at its own size");
        });
    }

    // Moved to another scope, a text answers its styles from there.
    [TestCase("block")]
    [TestCase("run")]
    [TestCase("box")]
    public void ATextMovedToAnotherScope_TakesItsStylesFromThere(string where)
    {
        var style = new CharacterStyle();
        Adamantium.UI.Core.Resources.ResourceResolver.SetDeferred(style, nameof(CharacterStyle.FontSize), "Size");
        StackPanel Scope(double size)
        {
            var panel = new StackPanel();
            var resources = new Adamantium.UI.Core.Resources.ResourceDictionary();
            resources.Add("Size", size);
            Adamantium.UI.Core.Resources.ResourceContext.SetResources(panel, resources);
            return panel;
        }

        var small = Scope(12);
        var large = Scope(30);
        Adamantium.UI.Core.IMeasurableComponent element;
        if (where == "box")
        {
            element = new TextBox { Text = "Moved", CharacterStyle = style };
        }
        else
        {
            var text = new TextBlock();
            if (where == "block")
            {
                text.CharacterStyle = style;
                text.Inlines.Add(new Run { Text = "Moved" });
            }
            else
            {
                text.Inlines.Add(new Run { Text = "Moved", CharacterStyle = style });
            }

            element = text;
        }

        small.Children.Add(element);
        var window = new Adamantium.UI.Controls.Window
        {
            Width = 400, Height = 300, Content = new StackPanel { Children = { small, large } },
        };
        void Update()
        {
            for (var i = 0; i < 5; i++)
            {
                Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            }
        }

        double Height() => element is TextBox box ? box.MeasureSurface(300).Height : element.DesiredSize.Height;
        Update();
        var before = Height();
        small.Children.Remove(element);
        large.Children.Add(element);
        Update();

        Assert.That(Height(), Is.GreaterThan(before + 10), "laid out at the larger scope's size");
    }

    [Test]
    public void ABaseGivenToAStyleInUse_AnswersItsReferences()
    {
        var used = new CharacterStyle();
        var text = new TextBlock();
        text.Inlines.Add(new Run { Text = "x", CharacterStyle = used });
        var panel = new StackPanel { Children = { text } };
        var resources = new Adamantium.UI.Core.Resources.ResourceDictionary();
        resources.Add("Size", 24.0);
        Adamantium.UI.Core.Resources.ResourceContext.SetResources(panel, resources);
        var window = new Adamantium.UI.Controls.Window { Width = 400, Height = 300, Content = panel };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        var late = new CharacterStyle();
        Adamantium.UI.Core.Resources.ResourceResolver.SetDeferred(late, nameof(CharacterStyle.FontSize), "Size");

        used.BasedOn = late;

        Assert.That(used.ResolvedFontSize(text), Is.EqualTo(24));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static System.WeakReference[] AddedAndRemoved(TextBlock text, Adamantium.UI.Controls.Window window)
    {
        var references = new System.WeakReference[50];
        for (var i = 0; i < references.Length; i++)
        {
            var run = new Run { Text = "x" };
            Adamantium.UI.Core.Resources.ResourceResolver.SetDeferred(run, nameof(Run.CharacterStyle), "Emphasis");
            text.Inlines.Add(run);
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            text.Inlines.Remove(run);
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            references[i] = new System.WeakReference(run);
        }

        return references;
    }

    [Test]
    public void InlinesThatReferencedAResource_AreNotKeptByTheirBlock()
    {
        var text = new TextBlock();
        var panel = new StackPanel { Children = { text } };
        var resources = new Adamantium.UI.Core.Resources.ResourceDictionary();
        resources.Add("Emphasis", new CharacterStyle { FontSize = 20 });
        Adamantium.UI.Core.Resources.ResourceContext.SetResources(panel, resources);
        var window = new Adamantium.UI.Controls.Window { Width = 400, Height = 300, Content = panel };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        var references = AddedAndRemoved(text, window);
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();

        Assert.That(System.Linq.Enumerable.Count(references, reference => reference.IsAlive), Is.Zero);
    }
}
