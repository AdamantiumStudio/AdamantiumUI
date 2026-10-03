using System.Collections.ObjectModel;
using Adamantium.MVVM;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Menus tab: a right-click ContextMenu whose whole tree - nested submenus and separators - comes from this
/// view-model (<see cref="MenuItems"/>), projected by a HierarchicalDataTemplate. Picking a leaf runs its command, which
/// records it in <see cref="PickedItem"/> so the binding round-trip is visible.</summary>
[ViewModel]
public partial class MenusViewModel : TabPageViewModel
{
    /// <summary>What was picked last: the row's title and, for a row in a submenu, the title of the row it hangs off.
    /// Each a phrase key, or a name said as it is.</summary>
    [Bindable] private MenuPick _pick = MenuPick.Nothing;

    [Bindable] private string _pickedItem;

    [Bindable] private string _pickedFrom;

    /// <summary>The menu tree, built in the VM. The view binds ContextMenu.ItemsSource to it.</summary>
    public ObservableCollection<MenuNode> MenuItems { get; }

    public MenusViewModel() : base("Menus")
    {
        // A deliberately long submenu (taller than the window) to show the flyout scrolls instead of clipping - and each
        // entry has its OWN submenu, so a submenu opens off a SCROLLED row (it anchors to the row's live position).
        var recent = new MenuNode { Title = nameof(MenusStrings.RecentFiles) };
        for (var i = 1; i <= 40; i++)
        {
            var name = $"project_{i:00}.auml";
            recent.Children.Add(MenuNode.Parent(name,
                MenuNode.Leaf(nameof(MenusStrings.Open), new AdamantiumCommand(() => Record(nameof(MenusStrings.Open), name))),
                MenuNode.Leaf(nameof(MenusStrings.Reveal), new AdamantiumCommand(() => Record(nameof(MenusStrings.Reveal), name))),
                MenuNode.Divider(),
                MenuNode.Leaf(nameof(MenusStrings.RemoveFromList), new AdamantiumCommand(() => Record(nameof(MenusStrings.RemoveFromList), name)))));
        }

        MenuItems =
        [
            MenuNode.Leaf(nameof(MenusStrings.Cut), new AdamantiumCommand(() => Record(nameof(MenusStrings.Cut))), "Ctrl+X"),
            MenuNode.Leaf(nameof(MenusStrings.Copy), new AdamantiumCommand(() => Record(nameof(MenusStrings.Copy))), "Ctrl+C"),
            MenuNode.Leaf(nameof(MenusStrings.Paste), new AdamantiumCommand(() => Record(nameof(MenusStrings.Paste))), "Ctrl+V"),
            MenuNode.Divider(),
            MenuNode.Parent(nameof(MenusStrings.Zoom),
                MenuNode.Leaf(nameof(MenusStrings.ResetZoom), new AdamantiumCommand(() => Record(nameof(MenusStrings.ResetZoom), nameof(MenusStrings.Zoom)))),
                MenuNode.Leaf(nameof(MenusStrings.FitToWindow), new AdamantiumCommand(() => Record(nameof(MenusStrings.FitToWindow), nameof(MenusStrings.Zoom)))),
                MenuNode.Divider(),
                MenuNode.Parent(nameof(MenusStrings.Presets),
                    MenuNode.Leaf("50%", new AdamantiumCommand(() => Record("50%", nameof(MenusStrings.Zoom)))),
                    MenuNode.Leaf("200%", new AdamantiumCommand(() => Record("200%", nameof(MenusStrings.Zoom)))),
                    MenuNode.Leaf("400%", new AdamantiumCommand(() => Record("400%", nameof(MenusStrings.Zoom)))))),
            MenuNode.Parent(nameof(MenusStrings.Arrange),
                MenuNode.Leaf(nameof(MenusStrings.BringToFront), new AdamantiumCommand(() => Record(nameof(MenusStrings.BringToFront), nameof(MenusStrings.Arrange)))),
                MenuNode.Leaf(nameof(MenusStrings.SendToBack), new AdamantiumCommand(() => Record(nameof(MenusStrings.SendToBack), nameof(MenusStrings.Arrange))))),
            recent,
            MenuNode.Divider(),
            MenuNode.Leaf(nameof(MenusStrings.Properties), new AdamantiumCommand(() => Record(nameof(MenusStrings.Properties)))),
        ];
    }

    private void Record(string item, string from = null)
    {
        PickedItem = item;
        PickedFrom = from;
        Pick = from == null ? MenuPick.Item : MenuPick.ItemFrom;
    }
}

/// <summary>What the menus page has to report about the last pick.</summary>
public enum MenuPick
{
    Nothing,
    Item,
    ItemFrom,
}
