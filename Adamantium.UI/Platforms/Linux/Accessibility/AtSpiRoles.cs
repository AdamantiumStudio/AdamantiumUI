using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Platforms.Linux.Accessibility;

/// <summary>What an automation peer is to AT-SPI: its role, the role's name and its state set.</summary>
internal static class AtSpiRoles
{
    /// <summary>The role of <paramref name="peer"/>: by its control type, and by what it does where one type covers two
    /// roles - a button that toggles, a menu item that is checked, a password box.</summary>
    public static AtSpiRole RoleOf(AutomationPeer peer) => peer.ControlType switch
    {
        AutomationControlType.Button when peer.GetPattern(PatternId.Toggle) != null => AtSpiRole.ToggleButton,
        AutomationControlType.Button => AtSpiRole.PushButton,
        AutomationControlType.Calendar => AtSpiRole.Calendar,
        AutomationControlType.CheckBox => AtSpiRole.CheckBox,
        AutomationControlType.ComboBox => AtSpiRole.ComboBox,
        AutomationControlType.DataGrid or AutomationControlType.Table => AtSpiRole.Table,
        AutomationControlType.DataItem => AtSpiRole.TableRow,
        AutomationControlType.Document => AtSpiRole.DocumentFrame,
        AutomationControlType.Edit when peer.IsPassword => AtSpiRole.PasswordText,
        AutomationControlType.Edit => AtSpiRole.Entry,
        AutomationControlType.Group => AtSpiRole.Grouping,
        AutomationControlType.Header => AtSpiRole.Header,
        AutomationControlType.HeaderItem => AtSpiRole.ColumnHeader,
        AutomationControlType.Hyperlink => AtSpiRole.Link,
        AutomationControlType.Image => AtSpiRole.Image,
        AutomationControlType.List => AtSpiRole.List,
        AutomationControlType.ListItem => AtSpiRole.ListItem,
        AutomationControlType.Menu => AtSpiRole.Menu,
        AutomationControlType.MenuBar => AtSpiRole.MenuBar,
        AutomationControlType.MenuItem when peer.GetPattern(PatternId.Toggle) != null => AtSpiRole.CheckMenuItem,
        AutomationControlType.MenuItem => AtSpiRole.MenuItem,
        AutomationControlType.ProgressBar => AtSpiRole.ProgressBar,
        AutomationControlType.RadioButton => AtSpiRole.RadioButton,
        AutomationControlType.ScrollBar => AtSpiRole.ScrollBar,
        AutomationControlType.Separator => AtSpiRole.Separator,
        AutomationControlType.Slider => AtSpiRole.Slider,
        AutomationControlType.Spinner => AtSpiRole.SpinButton,
        AutomationControlType.SplitButton => AtSpiRole.PushButtonMenu,
        AutomationControlType.StatusBar => AtSpiRole.StatusBar,
        AutomationControlType.Tab => AtSpiRole.PageTabList,
        AutomationControlType.TabItem => AtSpiRole.PageTab,
        AutomationControlType.Text => AtSpiRole.Label,
        AutomationControlType.Thumb => AtSpiRole.Filler,
        AutomationControlType.TitleBar => AtSpiRole.TitleBar,
        AutomationControlType.ToolBar => AtSpiRole.ToolBar,
        AutomationControlType.ToolTip => AtSpiRole.ToolTip,
        AutomationControlType.Tree => AtSpiRole.Tree,
        AutomationControlType.TreeItem => AtSpiRole.TreeItem,
        AutomationControlType.Window when WindowOf(peer) is { IsModal: true } => AtSpiRole.Dialog,
        AutomationControlType.Window => AtSpiRole.Frame,
        _ => AtSpiRole.Panel,
    };

    /// <summary>The name AT-SPI gives <paramref name="role"/>, as libatspi spells it.</summary>
    public static string NameOf(AtSpiRole role) => role switch
    {
        AtSpiRole.Calendar => RoleNames.Calendar,
        AtSpiRole.CheckBox => RoleNames.CheckBox,
        AtSpiRole.CheckMenuItem => RoleNames.CheckMenuItem,
        AtSpiRole.ColumnHeader => RoleNames.ColumnHeader,
        AtSpiRole.ComboBox => RoleNames.ComboBox,
        AtSpiRole.Dialog => RoleNames.Dialog,
        AtSpiRole.Filler => RoleNames.Filler,
        AtSpiRole.Frame => RoleNames.Frame,
        AtSpiRole.Image => RoleNames.Image,
        AtSpiRole.Label => RoleNames.Label,
        AtSpiRole.List => RoleNames.List,
        AtSpiRole.ListItem => RoleNames.ListItem,
        AtSpiRole.Menu => RoleNames.Menu,
        AtSpiRole.MenuBar => RoleNames.MenuBar,
        AtSpiRole.MenuItem => RoleNames.MenuItem,
        AtSpiRole.PageTab => RoleNames.PageTab,
        AtSpiRole.PageTabList => RoleNames.PageTabList,
        AtSpiRole.Panel => RoleNames.Panel,
        AtSpiRole.PasswordText => RoleNames.PasswordText,
        AtSpiRole.ProgressBar => RoleNames.ProgressBar,
        AtSpiRole.PushButton => RoleNames.PushButton,
        AtSpiRole.RadioButton => RoleNames.RadioButton,
        AtSpiRole.ScrollBar => RoleNames.ScrollBar,
        AtSpiRole.Separator => RoleNames.Separator,
        AtSpiRole.Slider => RoleNames.Slider,
        AtSpiRole.SpinButton => RoleNames.SpinButton,
        AtSpiRole.StatusBar => RoleNames.StatusBar,
        AtSpiRole.Table => RoleNames.Table,
        AtSpiRole.ToggleButton => RoleNames.ToggleButton,
        AtSpiRole.ToolBar => RoleNames.ToolBar,
        AtSpiRole.ToolTip => RoleNames.ToolTip,
        AtSpiRole.Tree => RoleNames.Tree,
        AtSpiRole.Header => RoleNames.Header,
        AtSpiRole.Application => RoleNames.Application,
        AtSpiRole.Entry => RoleNames.Entry,
        AtSpiRole.DocumentFrame => RoleNames.DocumentFrame,
        AtSpiRole.Link => RoleNames.Link,
        AtSpiRole.TableRow => RoleNames.TableRow,
        AtSpiRole.TreeItem => RoleNames.TreeItem,
        AtSpiRole.Grouping => RoleNames.Grouping,
        AtSpiRole.TitleBar => RoleNames.TitleBar,
        AtSpiRole.PushButtonMenu => RoleNames.PushButtonMenu,
        _ => RoleNames.Unknown,
    };

    private static class RoleNames
    {
        public const string Calendar = "calendar";
        public const string CheckBox = "check box";
        public const string CheckMenuItem = "check menu item";
        public const string ColumnHeader = "column header";
        public const string ComboBox = "combo box";
        public const string Dialog = "dialog";
        public const string Filler = "filler";
        public const string Frame = "frame";
        public const string Image = "image";
        public const string Label = "label";
        public const string List = "list";
        public const string ListItem = "list item";
        public const string Menu = "menu";
        public const string MenuBar = "menu bar";
        public const string MenuItem = "menu item";
        public const string PageTab = "page tab";
        public const string PageTabList = "page tab list";
        public const string Panel = "panel";
        public const string PasswordText = "password text";
        public const string ProgressBar = "progress bar";
        public const string PushButton = "button";
        public const string RadioButton = "radio button";
        public const string ScrollBar = "scroll bar";
        public const string Separator = "separator";
        public const string Slider = "slider";
        public const string SpinButton = "spin button";
        public const string StatusBar = "status bar";
        public const string Table = "table";
        public const string ToggleButton = "toggle button";
        public const string ToolBar = "tool bar";
        public const string ToolTip = "tool tip";
        public const string Tree = "tree";
        public const string Header = "header";
        public const string Application = "application";
        public const string Entry = "entry";
        public const string DocumentFrame = "document frame";
        public const string Link = "link";
        public const string TableRow = "table row";
        public const string TreeItem = "tree item";
        public const string Grouping = "grouping";
        public const string TitleBar = "title bar";
        public const string PushButtonMenu = "push button menu";
        public const string Unknown = "unknown";
    }

    /// <summary>The state set of <paramref name="peer"/> as AT-SPI sends it: two words of bits, the first for states
    /// 0 to 31.</summary>
    public static uint[] StatesOf(AutomationPeer peer)
    {
        var bits = 0UL;
        if (peer.IsEnabled)
        {
            bits |= Bit(AtSpiState.Enabled) | Bit(AtSpiState.Sensitive);
        }

        if (!peer.IsOffscreen)
        {
            bits |= Bit(AtSpiState.Visible) | Bit(AtSpiState.Showing);
        }

        if (peer.IsKeyboardFocusable)
        {
            bits |= Bit(AtSpiState.Focusable);
        }

        if (peer.HasKeyboardFocus)
        {
            bits |= Bit(AtSpiState.Focused);
        }

        if (peer.GetPattern(PatternId.Toggle) is IToggleProvider toggle)
        {
            bits |= Bit(AtSpiState.Checkable) | toggle.ToggleState switch
            {
                ToggleState.On => Bit(AtSpiState.Checked),
                ToggleState.Indeterminate => Bit(AtSpiState.Indeterminate),
                _ => 0,
            };
        }

        if (peer.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider item)
        {
            bits |= Bit(AtSpiState.Selectable) | (item.IsSelected ? Bit(AtSpiState.Selected) : 0);
        }

        if (peer.GetPattern(PatternId.Selection) is ISelectionProvider { CanSelectMultiple: true })
        {
            bits |= Bit(AtSpiState.MultiSelectable);
        }

        if (peer.GetPattern(PatternId.ExpandCollapse) is IExpandCollapseProvider expander &&
            expander.ExpandCollapseState != ExpandCollapseState.LeafNode)
        {
            bits |= Bit(AtSpiState.Expandable) | Bit(expander.ExpandCollapseState == ExpandCollapseState.Collapsed
                ? AtSpiState.Collapsed
                : AtSpiState.Expanded);
        }

        if (peer.GetPattern(PatternId.Value) is IValueProvider value)
        {
            bits |= value.IsReadOnly ? Bit(AtSpiState.ReadOnly) : Bit(AtSpiState.Editable);
        }

        if (peer.ControlType == AutomationControlType.Document ||
            peer.ControlType == AutomationControlType.Edit && peer.GetPattern(PatternId.Text) is ITextProvider { IsMultiline: true })
        {
            bits |= Bit(AtSpiState.MultiLine);
        }
        else if (peer.ControlType == AutomationControlType.Edit)
        {
            bits |= Bit(AtSpiState.SingleLine);
        }

        if (WindowOf(peer) is { } window)
        {
            bits |= (window.IsActive ? Bit(AtSpiState.Active) : 0) | (window.IsModal ? Bit(AtSpiState.Modal) : 0);
        }

        return [(uint)bits, (uint)(bits >> 32)];
    }

    private static ulong Bit(AtSpiState state) => 1UL << (int)state;

    private static IWindow WindowOf(AutomationPeer peer) => (peer as UIComponentAutomationPeer)?.Owner as IWindow;
}
