using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Platforms.MacOS.Accessibility;

/// <summary>What an automation peer is to the Mac's accessibility: its role and subrole, AppKit's names for them.</summary>
internal static class MacOSAccessibilityRoles
{
    /// <summary>The role of <paramref name="peer"/>: by its control type, and by what it does where one type covers two
    /// roles - a button that toggles, a password box.</summary>
    public static string RoleOf(AutomationPeer peer) => peer.ControlType switch
    {
        AutomationControlType.Button when peer.GetPattern(PatternId.Toggle) != null => "AXCheckBox",
        AutomationControlType.Button or AutomationControlType.SplitButton => "AXButton",
        AutomationControlType.Calendar => "AXGroup",
        AutomationControlType.CheckBox => "AXCheckBox",
        AutomationControlType.ComboBox => peer.GetPattern(PatternId.Value) != null ? "AXComboBox" : "AXPopUpButton",
        AutomationControlType.DataGrid or AutomationControlType.Table => "AXTable",
        AutomationControlType.DataItem => "AXRow",
        AutomationControlType.Document => "AXTextArea",
        AutomationControlType.Edit when peer.IsPassword => "AXTextField",
        AutomationControlType.Edit => peer.GetPattern(PatternId.Text) is ITextProvider { IsMultiline: true } ? "AXTextArea" : "AXTextField",
        AutomationControlType.Group or AutomationControlType.Pane or AutomationControlType.Custom => "AXGroup",
        AutomationControlType.Header => "AXGroup",
        AutomationControlType.HeaderItem => "AXButton",
        AutomationControlType.Hyperlink => "AXLink",
        AutomationControlType.Image => "AXImage",
        AutomationControlType.List => "AXList",
        AutomationControlType.ListItem => "AXStaticText",
        AutomationControlType.Menu => "AXMenu",
        AutomationControlType.MenuBar => "AXMenuBar",
        AutomationControlType.MenuItem => "AXMenuItem",
        AutomationControlType.ProgressBar => "AXProgressIndicator",
        AutomationControlType.RadioButton => "AXRadioButton",
        AutomationControlType.ScrollBar => "AXScrollBar",
        AutomationControlType.Separator => "AXSplitter",
        AutomationControlType.Slider => "AXSlider",
        AutomationControlType.Spinner => "AXIncrementor",
        AutomationControlType.StatusBar => "AXGroup",
        AutomationControlType.Tab => "AXTabGroup",
        AutomationControlType.TabItem => "AXRadioButton",
        AutomationControlType.Text => "AXStaticText",
        AutomationControlType.Thumb => "AXValueIndicator",
        AutomationControlType.TitleBar => "AXGroup",
        AutomationControlType.ToolBar => "AXToolbar",
        AutomationControlType.ToolTip => "AXHelpTag",
        AutomationControlType.Tree => "AXOutline",
        AutomationControlType.TreeItem => "AXRow",
        AutomationControlType.Window => "AXGroup",
        _ => "AXUnknown"
    };

    /// <summary>The subrole that says more than the role does, or null: a password, a tab, a row of an outline.</summary>
    public static string SubroleOf(AutomationPeer peer) => peer.ControlType switch
    {
        AutomationControlType.Edit when peer.IsPassword => "AXSecureTextField",
        AutomationControlType.Button when peer.GetPattern(PatternId.Toggle) != null => "AXToggle",
        AutomationControlType.TabItem => "AXTabButton",
        AutomationControlType.TreeItem => "AXOutlineRow",
        AutomationControlType.DataItem => "AXTableRow",
        AutomationControlType.ToolTip => null,
        _ => null
    };

    /// <summary>Whether VoiceOver stops at <paramref name="peer"/>: a container with no name of its own it walks through to
    /// what is in it.</summary>
    public static bool IsElement(AutomationPeer peer) => peer.ControlType switch
    {
        AutomationControlType.Group or AutomationControlType.Pane or AutomationControlType.Custom
            or AutomationControlType.Window => !string.IsNullOrEmpty(peer.Name),
        _ => true
    };
}
