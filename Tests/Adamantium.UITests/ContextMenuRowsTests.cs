using System.Collections.Generic;
using Adamantium.Core.Commands;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// A menu built from data makes every row a MenuItem, whatever its template: the template draws what the row says, the
/// container style says what it does. A plain DataTemplate used to leave the rows bare content - no hover, no check, no
/// command - and only a HierarchicalDataTemplate made menu rows.
/// </summary>
[TestFixture]
public class ContextMenuRowsTests
{
    private sealed class Row
    {
        public string Name { get; init; }

        public ICommand Command { get; init; }
    }

    private sealed class Rule : ISeparatorItem
    {
        public bool IsSeparator => true;
    }

    private static DataTemplate Label() => new(() => new TemplateResult { RootComponent = new TextBlock() });

    private static Style RowStyle()
    {
        var style = new Style();
        style.Selector.Types.Add(typeof(MenuItem));
        style.Setters.Add(new Setter("Command", new Binding(nameof(Row.Command))));
        style.Setters.Add(new Setter("IsCheckable", true));
        return style;
    }

    private static ContextMenu MenuOf(IList<object> rows, DataTemplate template, Style rowStyle = null) =>
        new() { ItemsSource = rows, ItemTemplate = template, ItemContainerStyle = rowStyle };

    private static IUIComponent RowOf(ContextMenu menu, int index)
    {
        var row = menu.ItemContainerGenerator.Realize(index);
        var window = new Window { Width = 400, Height = 300, Content = row };
        for (var i = 0; i < 3; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return row;
    }

    [Test]
    public void APlainTemplate_StillMakesMenuRows_AndDrawsTheirHeaders()
    {
        var row = new Row { Name = "Surface" };
        var template = Label();
        var container = RowOf(MenuOf([row], template), 0) as MenuItem;

        Assert.That(container, Is.Not.Null, "a menu row, not bare content");
        Assert.Multiple(() =>
        {
            Assert.That(container.Header, Is.SameAs(row));
            Assert.That(container.HeaderTemplate, Is.SameAs(template));
            Assert.That(container.DataContext, Is.SameAs(row));
        });
    }

    [Test]
    public void TheContainerStyle_SaysWhatEveryRowDoes()
    {
        var command = new SwitchableCommand { CanRun = true };
        var container = (MenuItem)RowOf(MenuOf([new Row { Name = "Surface", Command = command }], Label(), RowStyle()), 0);

        Assert.Multiple(() =>
        {
            Assert.That(container.Command, Is.SameAs(command));
            Assert.That(container.IsCheckable, Is.True);
        });
    }

    [Test]
    public void AnItemThatIsARule_StaysASeparator()
    {
        Assert.That(RowOf(MenuOf([new Row { Name = "Surface" }, new Rule()], Label()), 1), Is.InstanceOf<Separator>());
    }

    [Test]
    public void ASubmenuFromData_MakesMenuRowsToo()
    {
        var parent = new MenuItem { ItemTemplate = Label() };

        Assert.That(parent.GetContainerForItem(new Row { Name = "Surface" }), Is.InstanceOf<MenuItem>());
    }
}
