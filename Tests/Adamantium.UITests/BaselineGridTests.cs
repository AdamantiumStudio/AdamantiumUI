using System.Collections.Generic;
using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// A baseline grid set on an element: the text blocks under it, whatever their size and wherever they stand, put their
// baselines on lines that start at that element's top; scrolling does not move them off it.
[TestFixture]
public class BaselineGridTests
{
    private const double Step = 24;

    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful.";

    private static void Update(Window window)
    {
        for (var i = 0; i < 6; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static StackPanel Page(out TextBlock[] blocks)
    {
        blocks =
        [
            new TextBlock { Text = "A Heading", FontSize = 30 },
            new TextBlock { Text = Prose, FontSize = 16, TextWrapping = TextWrapping.WrapByWords, Margin = new Thickness(0, 7, 0, 0) },
            new TextBlock { Text = Prose, FontSize = 13, TextWrapping = TextWrapping.WrapByWords, Margin = new Thickness(0, 3, 0, 0) },
        ];
        var panel = new StackPanel { Width = 300 };
        foreach (var block in blocks)
        {
            panel.Children.Add(block);
        }

        return panel;
    }

    // The baselines of a block, from the top of the element above it.
    private static double[] Baselines(TextBlock block, IUIComponent from)
    {
        double top = 0;
        for (IUIComponent element = block; !ReferenceEquals(element, from); element = element.VisualParent)
        {
            top += element.Bounds.Y;
        }

        return Enumerable.Range(0, block.Layout.LineCount).Select(i => top + block.Layout.GetLine(i).Baseline).ToArray();
    }

    private static IEnumerable<TextBlock> Blocks(IUIComponent element) => element is TextBlock block
        ? [block]
        : element.VisualChildren.SelectMany(Blocks);

    private static bool OnGrid(double baseline, double offset = 0)
    {
        var phase = ((baseline - offset) % Step + Step) % Step;
        return phase < 0.51 || Step - phase < 0.51;
    }

    // A text box lays its lines out inside its own surface: they stand on the grid from where that surface is.
    [Test]
    public void TheLinesOfATextBox_StandOnTheGrid()
    {
        var presenter = new TextPresenter();
        var box = new TextBox
        {
            Text = "one\ntwo\nthree",
            AcceptsReturn = true,
            FontSize = 16,
            Template = new ControlTemplate(() =>
            {
                var root = new Border { Padding = new Thickness(0, 5, 0, 0), Child = presenter };
                var result = new TemplateResult { RootComponent = root };
                result.RegisterName("PART_TextPresenter", presenter);
                return result;
            }),
        };
        var panel = new StackPanel { Width = 300, BaselineGridStep = Step, Children = { new Border { Height = 7 }, box } };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        Update(window);
        double top = 0;
        for (IUIComponent element = presenter; !ReferenceEquals(element, panel); element = element.VisualParent)
        {
            top += element.Bounds.Y;
        }

        var baselines = Enumerable.Range(0, box.Layout.LineCount)
            .Select(i => top + box.Layout.GetLine(i).Baseline).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(baselines, Has.Length.EqualTo(3));
            Assert.That(baselines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", baselines));
        });
    }

    [Test]
    public void TheBlocksUnderTheGrid_StandOnItsLines()
    {
        var panel = Page(out var blocks);
        panel.BaselineGridStep = Step;
        var window = new Window { Width = 800, Height = 600, Content = panel };
        Update(window);
        var baselines = blocks.SelectMany(block => Baselines(block, panel)).ToArray();

        Assert.That(baselines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", baselines));
    }

    [Test]
    public void TheGridsOffset_IsFromTheTopOfTheElementItIsSetOn()
    {
        var panel = Page(out var blocks);
        panel.BaselineGridStep = Step;
        panel.BaselineGridOffset = 5;
        var window = new Window
        {
            Width = 800, Height = 600, Content = new StackPanel { Children = { new Border { Height = 11 }, panel } },
        };
        Update(window);
        var baselines = blocks.SelectMany(block => Baselines(block, panel)).ToArray();

        Assert.That(baselines.All(baseline => OnGrid(baseline, 5)), Is.True, string.Join(", ", baselines));
    }

    [Test]
    public void WithoutAGrid_TheBlocksAreAsBefore_AndAreSoAgainOnceItIsCleared()
    {
        var plainPanel = Page(out var plain);
        Update(new Window { Width = 800, Height = 600, Content = plainPanel });
        var panel = Page(out var blocks);
        var window = new Window { Width = 800, Height = 600, Content = panel };
        panel.BaselineGridStep = Step;
        Update(window);
        var gridded = blocks.SelectMany(block => Baselines(block, panel)).ToArray();
        panel.ClearValue(UIComponent.BaselineGridStepProperty);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(gridded, Is.Not.EqualTo(plain.SelectMany(block => Baselines(block, plainPanel)).ToArray()));
            Assert.That(blocks.SelectMany(block => Baselines(block, panel)),
                Is.EqualTo(plain.SelectMany(block => Baselines(block, plainPanel))).Within(1e-6));
        });
    }

    [Test]
    public void OnlyFirstLines_KeepTheRestAsTheyAre()
    {
        var panel = Page(out var blocks);
        panel.BaselineGridStep = Step;
        panel.BaselineGridFirstLineOnly = true;
        var window = new Window { Width = 800, Height = 600, Content = panel };
        Update(window);
        var lines = Baselines(blocks[2], panel);

        Assert.Multiple(() =>
        {
            Assert.That(blocks.All(block => OnGrid(Baselines(block, panel)[0])), Is.True, "every first line");
            Assert.That(lines.Length, Is.GreaterThan(1));
            Assert.That(OnGrid(lines[1]), Is.False, "a later line keeps its own spacing");
        });
    }

    [Test]
    public void ABlockMadeByATemplate_StandsOnTheGrid()
    {
        var presenter = new ContentPresenter
        {
            Content = Prose,
            ContentTemplate = new DataTemplate(() => new TemplateResult
            {
                RootComponent = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.WrapByWords },
            }),
        };
        var panel = new StackPanel { Width = 300, BaselineGridStep = Step, Children = { new Border { Height = 10 }, presenter } };
        var window = new Window { Width = 800, Height = 600, Content = panel };
        Update(window);
        var block = (TextBlock)presenter.VisualChildren.Single();
        block.Text = Prose;
        Update(window);
        var baselines = Baselines(block, panel);

        Assert.That(baselines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", baselines));
    }

    [Test]
    public void AChangedStep_PutsTheLinesOnTheNewGrid()
    {
        var panel = Page(out var blocks);
        panel.BaselineGridStep = 50;
        var window = new Window
        {
            Width = 800, Height = 600, Content = new StackPanel { Children = { new Border { Height = 10 }, panel } },
        };
        Update(window);
        panel.BaselineGridStep = Step;
        Update(window);
        var baselines = blocks.SelectMany(block => Baselines(block, panel)).ToArray();

        Assert.That(baselines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", baselines));
    }

    [Test]
    public void ManyBlocks_StandOnTheGrid_AfterOnePass()
    {
        var panel = new StackPanel { Width = 300, BaselineGridStep = Step };
        for (var i = 0; i < 40; i++)
        {
            panel.Children.Add(new TextBlock
            {
                Text = i % 3 == 0 ? "A heading" : Prose,
                FontSize = i % 3 == 0 ? 26 : 13 + i % 4,
                TextWrapping = TextWrapping.WrapByWords,
                Margin = new Thickness(0, i % 5, 0, 0),
            });
        }

        var window = new Window { Width = 800, Height = 4000, Content = panel };
        WindowExtension.UpdateTree(window);
        var baselines = panel.Children.Cast<TextBlock>().SelectMany(block => Baselines(block, panel)).ToArray();

        Assert.That(baselines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", baselines));
    }

    [TestCase(VerticalAlignment.Bottom)]
    [TestCase(VerticalAlignment.Center)]
    public void ABlockPlacedByItsOwnHeight_LetsTheLayoutSettle(VerticalAlignment alignment)
    {
        var block = new TextBlock { Text = "One line", FontSize = 16, VerticalAlignment = alignment };
        var window = new Window
        {
            Width = 800, Height = 600, Content = new Border { Height = 203, BaselineGridStep = Step, Child = block },
        };
        for (var i = 0; i < 40; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        var before = MeasurableUIComponent.TotalMeasureCores;
        Update(window);

        Assert.That(MeasurableUIComponent.TotalMeasureCores, Is.EqualTo(before), "nothing measures any more");
    }

    [Test]
    public void ARowOfAVirtualizedList_StandsOnTheGrid_AsSoonAsItIsScrolledTo()
    {
        var list = new ItemsControl
        {
            ItemsSource = Enumerable.Range(0, 500).Cast<object>().ToList(),
            ItemTemplate = new DataTemplate(() => new TemplateResult
            {
                RootComponent = new StackPanel { Children = { new TextBlock { Text = "A row", FontSize = 13 } } },
            }),
            Template = new ControlTemplate(() =>
            {
                var items = new ItemsPresenter();
                var result = new TemplateResult { RootComponent = items };
                result.RegisterName("PART_ItemsPresenter", items);
                return result;
            }),
        };
        var scroller = new ScrollContentPresenter { CanContentScroll = true, Height = 300, Content = list };
        var outer = new StackPanel { BaselineGridStep = Step, Children = { new Border { Height = 7 }, scroller } };
        var window = new Window { Width = 400, Height = 600, Content = outer };
        Update(window);
        ((IScrollableContent)scroller).SetOffset(new Vector2(0, 1250));
        WindowExtension.UpdateTree(window);
        var rows = list.ItemContainerGenerator.RealizedIndices
            .Select(index => list.ItemContainerGenerator.ContainerFromIndex(index))
            .SelectMany(Blocks).ToArray();
        var baselines = rows.SelectMany(block => Baselines(block, list.ItemsHostPanel))
            .Select(baseline => baseline + 7).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Is.Not.Empty);
            Assert.That(baselines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", baselines));
        });
    }

    [Test]
    public void BlocksInTheAutoRowsOfAGrid_SettleOnTheGrid()
    {
        var grid = new Grid { Width = 300, BaselineGridStep = Step };
        for (var i = 0; i < 20; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var block = new TextBlock
            {
                Text = i % 4 == 0 ? "A heading" : Prose,
                FontSize = i % 4 == 0 ? 26 : 12 + i % 5,
                TextWrapping = TextWrapping.WrapByWords,
                Margin = new Thickness(0, i % 3, 0, 0),
            };
            Grid.SetRow(block, i);
            grid.Children.Add(block);
        }

        var window = new Window { Width = 800, Height = 4000, Content = grid };
        for (var i = 0; i < 40; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        var baselines = grid.Children.Cast<TextBlock>().SelectMany(block => Baselines(block, grid)).ToArray();

        Assert.That(baselines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", baselines));
    }

    [Test]
    public void TheRowsOfAVirtualizedList_KeepApart_OnTheGrid()
    {
        var list = new ItemsControl
        {
            ItemsSource = Enumerable.Range(0, 200).Cast<object>().ToList(),
            ItemTemplate = new DataTemplate(() => new TemplateResult
            {
                RootComponent = new TextBlock { Text = "A row", FontSize = 13 },
            }),
            Template = new ControlTemplate(() =>
            {
                var items = new ItemsPresenter();
                var result = new TemplateResult { RootComponent = items };
                result.RegisterName("PART_ItemsPresenter", items);
                return result;
            }),
        };
        var scroller = new ScrollContentPresenter { CanContentScroll = true, Height = 300, Content = list };
        var outer = new StackPanel { BaselineGridStep = Step, Children = { new Border { Height = 9 }, scroller } };
        var window = new Window { Width = 400, Height = 600, Content = outer };
        Update(window);
        var baselines = list.ItemContainerGenerator.RealizedIndices.OrderBy(index => index)
            .Select(index => list.ItemContainerGenerator.ContainerFromIndex(index))
            .SelectMany(Blocks).SelectMany(block => Baselines(block, list.ItemsHostPanel))
            .Select(baseline => baseline + 9).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(baselines.Length, Is.GreaterThan(3));
            Assert.That(baselines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", baselines));
            for (var i = 1; i < baselines.Length; i++)
            {
                Assert.That(baselines[i], Is.GreaterThan(baselines[i - 1] + 1), string.Join(", ", baselines));
            }
        });
    }

    [Test]
    public void TextInAPopup_UnderAPanelThatDoesNotSayWhereItStands_SettlesOnTheGrid()
    {
        var block = new TextBlock { Text = Prose, FontSize = 16, TextWrapping = TextWrapping.WrapByWords };
        var grid = new Grid { Width = 300, BaselineGridStep = Step, RowDefinitions = { new RowDefinition { Height = new GridLength(13) }, new RowDefinition { Height = GridLength.Auto } } };
        Grid.SetRow(block, 1);
        grid.Children.Add(block);
        var popup = new Popup { Child = grid };
        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { popup } } };
        WindowExtension.UpdateTree(window);
        popup.IsOpen = true;
        for (var i = 0; i < 4; i++)
        {
            window.PopupLayer.UpdateLayout(new Size(800, 600));
            WindowExtension.UpdateTree(window);
        }

        var baselines = Baselines(block, grid);

        Assert.That(baselines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", baselines));
    }

    [Test]
    public void AGridSetAgainBelow_StartsAtThatElement_EvenWithTheSameStep()
    {
        var block = new TextBlock { Text = Prose, FontSize = 16, TextWrapping = TextWrapping.WrapByWords };
        var inner = new Grid { Margin = new Thickness(0, 10, 0, 0), Children = { block } };
        var outer = new StackPanel { Width = 300, BaselineGridStep = Step, Children = { inner } };
        var window = new Window { Width = 800, Height = 600, Content = outer };
        Update(window);
        inner.BaselineGridStep = Step;
        Update(window);
        var baselines = Baselines(block, inner);

        Assert.That(baselines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", baselines));
    }

    [Test]
    public void TheRowsOfAListWithNoText_KeepTheirPitch_UnderAGrid()
    {
        var list = new ItemsControl
        {
            ItemsSource = Enumerable.Range(0, 100).Cast<object>().ToList(),
            ItemTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Height = 32 } }),
            Template = new ControlTemplate(() =>
            {
                var items = new ItemsPresenter();
                var result = new TemplateResult { RootComponent = items };
                result.RegisterName("PART_ItemsPresenter", items);
                return result;
            }),
        };
        var scroller = new ScrollContentPresenter { CanContentScroll = true, Height = 300, Content = list };
        var window = new Window
        {
            Width = 400, Height = 600, Content = new StackPanel { BaselineGridStep = Step, Children = { scroller } },
        };
        Update(window);
        var tops = list.ItemContainerGenerator.RealizedIndices.OrderBy(index => index).Take(3)
            .Select(index => list.ItemContainerGenerator.ContainerFromIndex(index).Bounds.Y).ToArray();

        Assert.That(tops[1] - tops[0], Is.EqualTo(32).Within(0.01));
    }

    [Test]
    public void TextInAPopup_StandsOnTheGrid()
    {
        var block = new TextBlock { Text = Prose, FontSize = 16, TextWrapping = TextWrapping.WrapByWords };
        var card = new StackPanel
        {
            Width = 300, BaselineGridStep = Step,
            Children = { new Border { Height = 10 }, new Border { Padding = new Thickness(0, 5, 0, 0), Child = block } },
        };
        var popup = new Popup { Child = card };
        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { popup } } };
        WindowExtension.UpdateTree(window);
        popup.IsOpen = true;
        for (var i = 0; i < 4; i++)
        {
            window.PopupLayer.UpdateLayout(new Size(800, 600));
            WindowExtension.UpdateTree(window);
        }

        var baselines = Baselines(block, card);

        Assert.That(baselines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", baselines));
    }

    [Test]
    public void Scrolling_DoesNotMoveTheTextOffTheGrid()
    {
        var panel = Page(out var blocks);
        var scroller = new ScrollContentPresenter { Height = 80, Content = panel };
        var outer = new StackPanel { BaselineGridStep = Step, Children = { new Border { Height = 9 }, scroller } };
        var window = new Window { Width = 800, Height = 600, Content = outer };
        Update(window);
        var before = blocks.SelectMany(block => Baselines(block, panel)).ToArray();
        var fromAbove = blocks.SelectMany(block => Baselines(block, outer)).ToArray();
        scroller.SetOffset(new Vector2(0, 13));
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(fromAbove.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", fromAbove));
            Assert.That(blocks.SelectMany(block => Baselines(block, panel)), Is.EqualTo(before).Within(1e-6));
        });
    }
}
