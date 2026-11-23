using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// A bar down the side is a column as wide as a button: a command that fits stands as it is - in its own narrow form - and
// a wider one is folded into a button that opens the command itself on a card beside the column; a label goes with its
// command. Along a bar, and in its overflow menu, every command stands as it is - in each theme's own style.
[TestFixture]
public class ToolBarVerticalFormTests
{
    private static readonly System.Type[] StyleSets =
    [
        typeof(Adamantium.UI.Themes.FluentTheme.ToolBarStyleSet),
        typeof(Adamantium.UI.Themes.GraphiteTheme.ToolBarStyleSet),
        typeof(Adamantium.UI.Themes.MacOsTheme.MacOsToolBarStyleSet)
    ];

    private StyleSet _styleSet;

    private void Use(System.Type styleSet)
    {
        _styleSet = (StyleSet)System.Activator.CreateInstance(styleSet);
        _styleSet.Initialize(null);
    }

    private void Style(IFundamentalUIComponent element)
    {
        foreach (var style in _styleSet.Styles.Where(style => style.Selector.Match(element)))
        {
            style.Attach(element);
        }
    }

    private sealed class Form : System.ComponentModel.INotifyPropertyChanged
    {
        private string _author = "Ann";

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public string Author
        {
            get => _author;
            set
            {
                _author = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Author)));
            }
        }
    }

    private sealed class Side
    {
        public Window Window;
        public ToolBarTray Tray;
        public ToolBar Bar;
        public ToolBarGroup Group;
        public Button Bold;
        public TextBlock AuthorLabel;
        public TextBox Author;
        public TextBlock SizeLabel;
        public Button Size;
        public Form Data;
    }

    // A section of a bold button, an author field under its label and a narrow size button under its own, down the side.
    private Side Build(double height = 600, bool collapsed = false)
    {
        var side = new Side { Data = new Form() };
        side.Bold = new Button { Width = 30, Height = 24, Content = "B" };
        side.AuthorLabel = new TextBlock { Text = "Author" };
        side.Author = new TextBox { Width = 120 };
        side.Author.SetBinding(TextBox.TextProperty, new Binding(nameof(Form.Author)));
        AutomationProperties.SetLabeledBy(side.Author, side.AuthorLabel);
        side.SizeLabel = new TextBlock { Text = "Size" };
        side.Size = new Button { Width = 30, Height = 24, Content = "S" };
        AutomationProperties.SetLabeledBy(side.Size, side.SizeLabel);
        side.Group = new ToolBarGroup
        {
            Header = "Review",
            Items = { side.Bold, side.AuthorLabel, side.Author, side.SizeLabel, side.Size }
        };
        side.Bar = new ToolBar
        {
            Header = "Review", Items = { side.Group }, Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible
        };
        side.Tray = new ToolBarTray { Orientation = Orientation.Vertical, Items = { side.Bar } };
        side.Window = new Window
        {
            Width = 600, Height = height, DataContext = side.Data,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { side.Tray } }
        };
        Show(side);
        return side;
    }

    // The test has no window theme: each bar part is styled as its theme would, as it comes - a folded command's item
    // makes its button on the way.
    private void Show(Side side)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            foreach (var element in side.Window.GetVisualDescendants().OfType<IFundamentalUIComponent>()
                         .Where(element => element is ToolBarTray or ToolBar or ToolBarGroup or ToolBarItem &&
                                           ((Control)element).Template == null))
            {
                Style(element);
            }

            for (var i = 0; i < 3; i++)
            {
                WindowExtension.UpdateTree(side.Window);
            }
        }
    }

    private static ToolBarItem ItemOf(IUIComponent command) =>
        command.GetSelfAndVisualAncestors().OfType<ToolBarItem>().FirstOrDefault() ??
        ((IFundamentalUIComponent)command).GetLogicalAncestors().OfType<ToolBarItem>().First();

    private static ToolBarItem ItemHolding(ToolBarGroup group, object command) =>
        Enumerable.Range(0, group.Items.Count)
            .Select(i => group.ItemContainerGenerator.ContainerFromIndex(i))
            .OfType<ToolBarItem>()
            .Single(item => ReferenceEquals(item.Content, command));

    private static ContentPresenter Inline(ToolBarItem item) =>
        (ContentPresenter)item.GetTemplateChild("PART_ContentPresenter");

    private static Popup Card(ToolBarItem item) => (Popup)item.GetTemplateChild("PART_Card");

    [TestCaseSource(nameof(StyleSets))]
    public void AWideCommandDownTheSide_IsFoldedIntoAButtonOfItsInitials_AndAlongABarIsItselfAgain(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var author = ItemHolding(side.Group, side.Author);
        var folded = author.Form;
        var button = Inline(author).Content as ToolBarFoldButton;
        var columnWidth = side.Bar.RenderSize.Width;

        side.Tray.Orientation = Orientation.Horizontal;
        Show(side);

        Assert.Multiple(() =>
        {
            Assert.That(folded, Is.EqualTo(ToolBarItemForm.Folded));
            Assert.That(button, Is.Not.Null, "a button stands in its place");
            Assert.That(button?.Content, Is.EqualTo("Au"), "the first two letters of its label");
            Assert.That(button?.Header, Is.EqualTo("Author"));
            Assert.That(button?.GetAutomationPeer()?.Name, Is.EqualTo("Author"), "named by the label, not its letters");
            Assert.That(columnWidth, Is.LessThan(side.Author.Width), "the column is not as wide as the field");
            Assert.That(author.Form, Is.EqualTo(ToolBarItemForm.Shown));
            Assert.That(Inline(author).Content, Is.SameAs(side.Author), "along a bar the field is itself again");
            Assert.That(side.Author.GetVisualAncestors(), Does.Contain(author));
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void TheCard_HoldsTheCommandItself_UnderItsLabel_AlongABarAndStillBound(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var author = ItemHolding(side.Group, side.Author);

        author.IsCardOpen = true;
        Show(side);
        // The popup layer lays the card out on screen; the test window has none.
        Card(author).Child.Measure(new Adamantium.Mathematics.Size<double>(double.PositiveInfinity, double.PositiveInfinity));
        var cardPresenter = Card(author).FindContentChild("PART_CardPresenter") as ContentPresenter;
        var cardHeader = Card(author).FindContentChild("PART_CardHeader") as ContentPresenter;
        var fold = (ToolBarFoldButton)Inline(author).Content;
        side.Data.Author = "Margo";
        Show(side);
        var shownText = side.Author.Text;
        var orientation = ToolBar.GetCommandOrientation(side.Author);

        Assert.Multiple(() =>
        {
            Assert.That(Card(author).IsOpen, Is.True);
            Assert.That(fold.IsChecked, Is.True, "its button reads as pressed while the card is open");
            Assert.That(cardPresenter?.Content, Is.SameAs(side.Author), "the field itself, moved - not a copy");
            Assert.That(cardHeader?.Content, Is.EqualTo("Author"));
            Assert.That(orientation, Is.EqualTo(Orientation.Horizontal), "on the card it stands as along a bar");
            Assert.That(shownText, Is.EqualTo("Margo"), "its binding still runs");
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void TheCard_ClosedOrTheBarTurnedAlong_GivesTheCommandBackToTheBar(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var author = ItemHolding(side.Group, side.Author);
        author.IsCardOpen = true;
        Show(side);
        var cardPresenter = (ContentPresenter)Card(author).FindContentChild("PART_CardPresenter");

        author.IsCardOpen = false;
        Show(side);
        var closed = Card(author).IsOpen;
        author.IsCardOpen = true;
        Show(side);
        side.Tray.Orientation = Orientation.Horizontal;
        Show(side);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.False);
            Assert.That(author.IsCardOpen, Is.False, "a command along a bar has no card");
            Assert.That(Card(author).IsOpen, Is.False);
            Assert.That(cardPresenter.Content, Is.Null);
            Assert.That(Inline(author).Content, Is.SameAs(side.Author));
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ALabel_LeavesTheColumnWithItsCommand_AndComesBackAlongABar(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var authorLabel = ItemHolding(side.Group, side.AuthorLabel);
        var sizeLabel = ItemHolding(side.Group, side.SizeLabel);
        var size = ItemHolding(side.Group, side.Size);
        var down = (Author: authorLabel.Visibility, Size: sizeLabel.Visibility);
        var tip = ToolTipService.GetToolTip(size);
        side.SizeLabel.Text = "Font size";
        Show(side);
        var renamedTip = ToolTipService.GetToolTip(size);

        side.Tray.Orientation = Orientation.Horizontal;
        Show(side);

        Assert.Multiple(() =>
        {
            Assert.That(down.Author, Is.EqualTo(Visibility.Collapsed), "the folded field's label is on its card");
            Assert.That(down.Size, Is.EqualTo(Visibility.Collapsed), "the narrow command's label is its tooltip");
            Assert.That(size.Form, Is.EqualTo(ToolBarItemForm.Shown));
            Assert.That(tip, Is.EqualTo("Size"));
            Assert.That(renamedTip, Is.EqualTo("Font size"), "the label is followed, a language switched say");
            Assert.That(authorLabel.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(sizeLabel.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(ToolTipService.GetToolTip(size), Is.Null, "along a bar the label stands beside it");
        });
    }

    // A label tied to its command only after the bar stands down the side - a binding by element name that resolves once
    // the bar is in its tree - leaves the column with the command all the same.
    [TestCaseSource(nameof(StyleSets))]
    public void ALabelTiedAfterTheBarStands_LeavesTheColumnWithItsCommand(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build(collapsed: true);
        AutomationProperties.SetLabeledBy(side.Size, null);
        side.Bar.Visibility = Visibility.Visible;
        Show(side);

        AutomationProperties.SetLabeledBy(side.Size, side.SizeLabel);
        Show(side);
        var sizeLabel = ItemHolding(side.Group, side.SizeLabel);
        var size = ItemHolding(side.Group, side.Size);

        Assert.Multiple(() =>
        {
            Assert.That(sizeLabel.Visibility, Is.EqualTo(Visibility.Collapsed), "the label leaves with its command");
            Assert.That(size.Form, Is.EqualTo(ToolBarItemForm.Shown));
            Assert.That(size.RenderSize.Width, Is.LessThanOrEqualTo(side.Bar.ColumnWidth + 0.01), "as wide as the column");
        });
    }

    // A bar down the side set to wrap, in a tray shorter than its sections: the section that does not fit stands in a
    // column beside, not past the tray's end and not in the menu.
    [TestCaseSource(nameof(StyleSets))]
    public void ABarDownTheSideThatWraps_PutsASectionThatDoesNotFitInANewColumn(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var second = new ToolBarGroup
        {
            Header = "More",
            Items =
            {
                new Button { Width = 30, Height = 24, Content = "1" }, new Button { Width = 30, Height = 24, Content = "2" },
                new Button { Width = 30, Height = 24, Content = "3" }
            }
        };
        side.Bar.Items.Add(second);
        side.Bar.CommandWrapping = CommandWrapping.Wrap;
        Show(side);
        var first = side.Group.RenderSize.Height;
        side.Tray.MaxHeight = first + 40;
        Show(side);

        Assert.Multiple(() =>
        {
            Assert.That(side.Bar.RenderSize.Height, Is.LessThanOrEqualTo(side.Tray.MaxHeight + 0.01), "within the tray");
            Assert.That(side.Bar.HasOverflowItems, Is.False, "wrapped, not in the menu");
            Assert.That(second.TranslatePoint(new Adamantium.Mathematics.Vector2<double>(0, 0), side.Bar).X,
                Is.GreaterThan(side.Group.TranslatePoint(new Adamantium.Mathematics.Vector2<double>(0, 0), side.Bar).X),
                "in a column beside");
        });
    }

    // A bar that wraps, a section of it longer than the whole tray: no column holds it, so it goes to the menu, and the
    // bar stays within the tray.
    [TestCaseSource(nameof(StyleSets))]
    public void ABarThatWraps_SendsASectionLongerThanTheTrayToTheMenu(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var small = new ToolBarGroup { Header = "Small", Items = { new Button { Width = 30, Height = 24, Content = "1" } } };
        side.Bar.Items.Insert(0, small);
        side.Bar.CommandWrapping = CommandWrapping.Wrap;
        Show(side);
        side.Tray.MaxHeight = side.Group.RenderSize.Height;
        Show(side);

        Assert.Multiple(() =>
        {
            Assert.That(side.Bar.RenderSize.Height, Is.LessThanOrEqualTo(side.Tray.MaxHeight + 0.01), "within the tray");
            Assert.That(side.Group.IsInOverflow, Is.True, "the long section is in the menu");
            Assert.That(small.IsInOverflow, Is.False, "what fits stays on the bar");
        });
    }

    // Down the side, along, down again: folded again, the command stands as a live button, not the one let go of.
    [TestCaseSource(nameof(StyleSets))]
    public void AFoldAfterAnUnfold_StandsAsALiveButton(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var author = ItemHolding(side.Group, side.Author);
        var first = (ToolBarFoldButton)Inline(author).Content;

        side.Tray.Orientation = Orientation.Horizontal;
        Show(side);
        side.Tray.Orientation = Orientation.Vertical;
        Show(side);
        var again = Inline(author).Content as ToolBarFoldButton;

        Assert.Multiple(() =>
        {
            Assert.That(again, Is.Not.Null);
            Assert.That(again?.IsDiscarded, Is.False, "a discarded button would be drawn in the window");
            Assert.That(again?.Content, Is.EqualTo("Au"));
            Assert.That(first.IsDiscarded, Is.True, "or this proves nothing: the presenter let it go");
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ANarrowCommand_StaysOnTheColumn_ToldItStandsDownTheSide(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var bold = ItemHolding(side.Group, side.Bold);

        Assert.Multiple(() =>
        {
            Assert.That(bold.Form, Is.EqualTo(ToolBarItemForm.Shown));
            Assert.That(Inline(bold).Content, Is.SameAs(side.Bold));
            Assert.That(ToolBar.GetCommandOrientation(side.Bold), Is.EqualTo(Orientation.Vertical),
                "its theme draws its narrow form from this");
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void TheVerticalForm_KeepsHidesOrFoldsACommand_AsItIsSet(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        ToolBar.SetVerticalForm(side.Author, ToolBarVerticalForm.Keep);
        ToolBar.SetVerticalForm(side.Bold, ToolBarVerticalForm.Fold);
        ToolBar.SetVerticalForm(side.Size, ToolBarVerticalForm.Hide);
        Show(side);
        var author = ItemHolding(side.Group, side.Author);
        var bold = ItemHolding(side.Group, side.Bold);
        var size = ItemHolding(side.Group, side.Size);

        Assert.Multiple(() =>
        {
            Assert.That(author.Form, Is.EqualTo(ToolBarItemForm.Shown), "kept, however wide");
            Assert.That(side.Bar.RenderSize.Width, Is.GreaterThanOrEqualTo(side.Author.Width), "the column grows to it");
            Assert.That(bold.Form, Is.EqualTo(ToolBarItemForm.Folded), "folded, however narrow");
            Assert.That(((ToolBarFoldButton)Inline(bold).Content).Content, Is.EqualTo("B"),
                "named by what it shows when it has no label");
            Assert.That(size.Form, Is.EqualTo(ToolBarItemForm.Hidden));
            Assert.That(size.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ACommandSetToFoldGivesItsIcon_ToItsButton(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var icon = new TextBlock { Text = "✎" };
        ToolBar.SetFoldedIcon(side.Author, icon);
        Show(side);
        var author = ItemHolding(side.Group, side.Author);

        Assert.That(((ToolBarFoldButton)Inline(author).Content).Content, Is.SameAs(icon));
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ASectionInTheOverflowMenu_StandsAsAlongABar(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var second = new ToolBarGroup { Header = "More" };
        var wide = new TextBox { Width = 120 };
        second.Items.Add(wide);
        ToolBar.SetOverflowMode(second, ToolBarOverflowMode.Always);
        side.Bar.Items.Add(second);
        Show(side);
        var foldedOnTheColumn = ItemHolding(side.Group, side.Author).Form;
        var item = ItemHolding(second, wide);

        Assert.Multiple(() =>
        {
            Assert.That(foldedOnTheColumn, Is.EqualTo(ToolBarItemForm.Folded), "or this proves nothing");
            Assert.That(side.Bar.HasOverflowItems, Is.True);
            Assert.That(item.Form, Is.EqualTo(ToolBarItemForm.Shown));
            Assert.That(ToolBar.GetCommandOrientation(wide), Is.EqualTo(Orientation.Horizontal));
        });
    }

    // A command of the bar's own, outside any section, goes to the menu as a section does: standing there as along a bar.
    [TestCaseSource(nameof(StyleSets))]
    public void ABarsOwnCommandInTheOverflowMenu_StandsAsAlongABar(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var wide = new TextBox { Width = 120 };
        ToolBar.SetVerticalForm(wide, ToolBarVerticalForm.Fold);
        ToolBar.SetOverflowMode(wide, ToolBarOverflowMode.Always);
        side.Bar.Items.Add(wide);
        Show(side);
        var item = ItemOf(wide);

        Assert.Multiple(() =>
        {
            Assert.That(side.Bar.HasOverflowItems, Is.True, "or this proves nothing");
            Assert.That(item.Form, Is.EqualTo(ToolBarItemForm.Shown), "in the menu, not folded");
            Assert.That(ToolBar.GetCommandOrientation(wide), Is.EqualTo(Orientation.Horizontal));
        });
    }

    // A wider column takes back what it folded only for want of room; a narrower one folds what no longer fits - in
    // a section as well as on the bar.
    [TestCaseSource(nameof(StyleSets))]
    public void AColumnMadeWiderOrNarrower_FoldsAgainWhatItMust(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var author = ItemHolding(side.Group, side.Author);
        var bold = ItemHolding(side.Group, side.Bold);
        var before = author.Form;

        side.Bar.ColumnWidth = 200;
        Show(side);
        var wider = author.Form;
        side.Bar.ColumnWidth = 20;
        Show(side);

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(ToolBarItemForm.Folded));
            Assert.That(wider, Is.EqualTo(ToolBarItemForm.Shown), "the field fits the wider column");
            Assert.That(author.Form, Is.EqualTo(ToolBarItemForm.Folded));
            Assert.That(bold.Form, Is.EqualTo(ToolBarItemForm.Folded), "30 wide, on a column of 20");
        });
    }

    // A card asked open before its command folded opens as it folds; the first press on the button then closes it.
    [TestCaseSource(nameof(StyleSets))]
    public void ACardAskedOpenBeforeTheFold_OpensWithIt(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        side.Tray.Orientation = Orientation.Horizontal;
        Show(side);
        var author = ItemHolding(side.Group, side.Author);
        author.IsCardOpen = true;
        Show(side);
        var along = Card(author).IsOpen;

        side.Tray.Orientation = Orientation.Vertical;
        Show(side);

        Assert.Multiple(() =>
        {
            Assert.That(along, Is.False, "along a bar a command has no card to open");
            Assert.That(author.Form, Is.EqualTo(ToolBarItemForm.Folded));
            Assert.That(Card(author).IsOpen, Is.True);
            Assert.That(((ToolBarFoldButton)Inline(author).Content).IsChecked, Is.True);
        });
    }

    // An icon given to a folded command reaches its button, the card left open.
    [TestCaseSource(nameof(StyleSets))]
    public void AnIconGivenToAFoldedCommand_LeavesItsCardOpen(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var author = ItemHolding(side.Group, side.Author);
        author.IsCardOpen = true;
        Show(side);

        var icon = new TextBlock { Text = "✎" };
        ToolBar.SetFoldedIcon(side.Author, icon);
        Show(side);

        Assert.Multiple(() =>
        {
            Assert.That(author.Form, Is.EqualTo(ToolBarItemForm.Folded));
            Assert.That(author.IsCardOpen, Is.True);
            Assert.That(((ToolBarFoldButton)Inline(author).Content).Content, Is.SameAs(icon));
        });
    }

    // On the bar's own column, the rule before a section stands only after a command left on the column: a label gone
    // with its command, and a command set to hide, are none.
    [TestCaseSource(nameof(StyleSets))]
    public void TheRuleBeforeASection_StandsOnlyAfterACommandLeftOnTheColumn(System.Type styleSet)
    {
        Use(styleSet);
        var label = new TextBlock { Text = "Zoom" };
        var zoom = new Slider { Width = 120 };
        AutomationProperties.SetLabeledBy(zoom, label);
        ToolBar.SetVerticalForm(zoom, ToolBarVerticalForm.Hide);
        var group = new ToolBarGroup { Header = "Edit", Items = { new Button { Width = 30, Height = 24, Content = "B" } } };
        var bar = new ToolBar { Header = "View", Items = { label, zoom, group } };
        var side = new Side
        {
            Bar = bar, Group = group,
            Tray = new ToolBarTray { Orientation = Orientation.Vertical, Items = { bar } }
        };
        side.Window = new Window
        {
            Width = 600, Height = 600,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { side.Tray } }
        };
        Show(side);

        Assert.Multiple(() =>
        {
            Assert.That(ItemOf(label).Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(ItemOf(zoom).Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(group.ShowsSeparator, Is.False, "nothing stands before the section on the column");
        });
    }

    // A label holding a control of its own names the command by its text; its control stays in it.
    [TestCaseSource(nameof(StyleSets))]
    public void ALabelHoldingAControl_NamesTheCommand_AndKeepsItsControl(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var text = new TextBlock { Text = "Writer" };
        var label = new ContentControl { Content = text };
        var field = new TextBox { Width = 120 };
        AutomationProperties.SetLabeledBy(field, label);
        side.Group.Items.Add(label);
        side.Group.Items.Add(field);
        Show(side);
        var item = ItemHolding(side.Group, field);
        item.IsCardOpen = true;
        Show(side);

        Assert.Multiple(() =>
        {
            Assert.That(item.Label, Is.EqualTo("Writer"));
            Assert.That(((ToolBarFoldButton)Inline(item).Content).Content, Is.EqualTo("Wr"));
            Assert.That(label.Content, Is.SameAs(text));
            Assert.That(text.GetVisualAncestors(), Does.Contain(label), "the label's text stays in the label");
        });
    }

    // Let go of when the bar makes its items again, an item holds neither its command nor a binding to it.
    [TestCaseSource(nameof(StyleSets))]
    public void AnItemLetGo_HoldsNeitherItsCommandNorABindingToIt(System.Type styleSet)
    {
        Use(styleSet);
        var side = Build();
        var old = ItemHolding(side.Group, side.Bold);
        var bound = BindingEngine.GetBindingExpression(old, UIComponent.VisibilityProperty) != null;

        side.Group.Items.Remove(side.Bold);
        Show(side);

        Assert.Multiple(() =>
        {
            Assert.That(bound, Is.True, "or this proves nothing");
            Assert.That(old.Content, Is.Null);
            Assert.That(BindingEngine.GetBindingExpression(old, UIComponent.VisibilityProperty), Is.Null,
                "no longer following the command");
        });
    }

    [Test]
    public void TheInitials_AreTheFirstTwoLettersOfTheLabel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ToolBarItem.Initials("Author"), Is.EqualTo("Au"));
            Assert.That(ToolBarItem.Initials("  zoom"), Is.EqualTo("Zo"));
            Assert.That(ToolBarItem.Initials("X"), Is.EqualTo("X"));
            Assert.That(ToolBarItem.Initials("A b"), Is.EqualTo("A"));
            Assert.That(ToolBarItem.Initials("…"), Is.Null);
        });
    }

    // The item is not a control of its own to automation: the command, or the button in its place, is.
    [Test]
    public void TheItem_IsNoElementToAutomation()
    {
        var item = new ToolBarItem { Content = new Button() };

        Assert.That(item.GetAutomationPeer(), Is.Null);
    }
}
