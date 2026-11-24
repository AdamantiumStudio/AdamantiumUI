using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.WebView.Accessibility;

namespace Adamantium.UI.WebView.Automation;

/// <summary>
/// The peer of a node of the page - a link, a field, a heading, a run of text - as Chromium's accessibility tree tells
/// it. What is done to it is done as a user does it: a click in its middle, keys typed into it, the wheel turned to bring
/// it in view - never a script acting on the page, which the page can tell apart.
/// </summary>
public class WebViewPageNodeAutomationPeer : AutomationPeer, IElementPartPeer, IInvokeProvider, IToggleProvider,
    IValueProvider, IScrollItemProvider
{
    private readonly WebViewAutomationPeer viewPeer;

    internal WebViewPageNodeAutomationPeer(WebViewAutomationPeer viewPeer, WebViewAccessibilityNode node)
    {
        this.viewPeer = viewPeer;
        Node = node;
    }

    internal WebViewAccessibilityNode Node { get; }

    private WebView View => viewPeer.View;

    private WebViewPageAccessibility Page => View.PageAccessibility;

    // Still the page's: a node of a document or a frame the page has left, or one taken out of it, is gone.
    private bool IsCurrent => Page is { } page && page.Holds(Node);

    public override AutomationControlType ControlType => Node.Role switch
    {
        "rootWebArea" or "webArea" => AutomationControlType.Document,
        "link" => AutomationControlType.Hyperlink,
        "button" or "popUpButton" or "toggleButton" => AutomationControlType.Button,
        "textField" or "searchBox" or "textFieldWithComboBox" => AutomationControlType.Edit,
        "checkBox" or "switch" or "menuItemCheckBox" => AutomationControlType.CheckBox,
        "radioButton" or "menuItemRadio" => AutomationControlType.RadioButton,
        "comboBoxSelect" or "comboBoxMenuButton" or "comboBoxGrouping" => AutomationControlType.ComboBox,
        "list" or "listBox" or "menuListPopup" or "descriptionList" => AutomationControlType.List,
        "listItem" or "listBoxOption" or "menuListOption" => AutomationControlType.ListItem,
        "heading" or "staticText" or "labelText" => AutomationControlType.Text,
        "image" or "svgRoot" => AutomationControlType.Image,
        "table" or "grid" or "treeGrid" => AutomationControlType.Table,
        "row" or "cell" or "gridCell" => AutomationControlType.DataItem,
        "columnHeader" or "rowHeader" => AutomationControlType.HeaderItem,
        "tabList" => AutomationControlType.Tab,
        "tab" => AutomationControlType.TabItem,
        "menu" => AutomationControlType.Menu,
        "menuBar" => AutomationControlType.MenuBar,
        "menuItem" => AutomationControlType.MenuItem,
        "tree" => AutomationControlType.Tree,
        "treeItem" => AutomationControlType.TreeItem,
        "slider" => AutomationControlType.Slider,
        "progressIndicator" or "meter" => AutomationControlType.ProgressBar,
        "scrollBar" => AutomationControlType.ScrollBar,
        "splitter" => AutomationControlType.Separator,
        "toolbar" => AutomationControlType.ToolBar,
        "tooltip" => AutomationControlType.ToolTip,
        "dialog" or "alertDialog" => AutomationControlType.Pane,
        _ => AutomationControlType.Group
    };

    public override string Name => Node.Name;

    public override string AutomationId => Node.HtmlId;

    public override string HelpText => Node.Description;

    public override string ClassName => Node.HtmlTag.Length > 0 ? Node.HtmlTag : Node.Role;

    // Of no size - an empty paragraph - it stands nowhere on screen.
    public override Rect BoundingRectangle =>
        IsCurrent && ViewRect() is { Width: > 0, Height: > 0 } rect ? viewPeer.PageToScreen(rect) : Rect.Empty;

    public override bool IsEnabled => Node.Restriction != "disabled" && viewPeer.IsEnabled;

    public override bool IsOffscreen
    {
        get
        {
            if (!IsCurrent || Node.IsInvisible || viewPeer.IsOffscreen)
            {
                return true;
            }

            var rect = ViewRect();
            var page = View.PageSize;
            return rect.Width <= 0 || rect.Height <= 0 || rect.Right <= 0 || rect.Bottom <= 0 ||
                   rect.X >= page.Width || rect.Y >= page.Height;
        }
    }

    public override bool HasKeyboardFocus => View.IsKeyboardFocused && IsCurrent && Page.Focused == Node;

    public override bool IsKeyboardFocusable => Node.IsFocusable;

    public override bool IsPassword => Node.InputType == "password";

    public override IReadOnlyList<AutomationPeer> GetChildren() =>
        IsCurrent ? Page.ShownChildren(Node).Select(child => (AutomationPeer)viewPeer.PeerOf(child)).ToList() : [];

    // The page's document is the view's; any other node, its nearest holder shown.
    public override AutomationPeer GetParent() =>
        Page?.ShownParent(Node) is { } holder ? viewPeer.PeerOf(holder) : viewPeer;

    /// <summary>Gives the view the keyboard; a field is clicked into, as a user gives it the caret - a link or a button
    /// is not, a click being what presses it.</summary>
    public override void SetFocus()
    {
        if (Node.IsEditable && IsCurrent)
        {
            View.ActInView(this, ClickMiddle);
            return;
        }

        View.Focus();
    }

    public override object GetPattern(PatternId pattern) => pattern switch
    {
        PatternId.Invoke => Node.Role is "link" or "button" or "popUpButton" or "menuItem" or "tab" ? this : null,
        PatternId.Toggle => Node.Role is "checkBox" or "switch" or "toggleButton" or "menuItemCheckBox" ? this : null,
        PatternId.Value => Node.Role is "textField" or "searchBox" or "textFieldWithComboBox" or "comboBoxSelect" ? this : null,
        PatternId.ScrollItem => this,
        _ => null
    };

    public UIComponent Element => View.PageHost;

    public Vector2<double>? Middle => IsCurrent && !IsOffscreen ? ViewMiddle() : null;

    /// <summary>Clicks its middle - once in view, brought there first when it stands out of it.</summary>
    public void Invoke() => View.ActInView(this, ClickMiddle);

    public ToggleState ToggleState => Node.CheckedState switch
    {
        "true" => ToggleState.On,
        "mixed" => ToggleState.Indeterminate,
        _ => ToggleState.Off
    };

    public void Toggle() => Invoke();

    public string Value => Node.Value;

    public bool IsReadOnly => Node.Restriction == "readOnly" || Node.Role == "comboBoxSelect";

    /// <summary>Clicks into the field and types <paramref name="value"/> over what it holds.</summary>
    public void SetValue(string value)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"{ControlType} \"{Name}\" is read-only.");
        }

        View.ActInView(this, () =>
        {
            ClickMiddle();
            View.TypeIntoPage(value ?? string.Empty);
        });
    }

    /// <summary>Turns the wheel to bring it to the middle of the page, when it stands out of it.</summary>
    public void ScrollIntoView() => TurnWheelTowards();

    /// <summary>Turns the wheel over the page towards it - false when there is no turn that brings it nearer: it stands
    /// in the page's height already, or is no longer the page's.</summary>
    internal bool TurnWheelTowards()
    {
        if (!IsCurrent)
        {
            return false;
        }

        var rect = ViewRect();
        var page = View.PageSize;
        if (rect.Y >= 0 && rect.Bottom <= page.Height)
        {
            return false;
        }

        View.ScrollPage(rect.Y + rect.Height / 2 - page.Height / 2);
        return true;
    }

    /// <summary>Whether it is still a node of the page shown.</summary>
    internal bool IsStillOnPage => IsCurrent;

    /// <summary>Whether its middle stands in the page's view, where a click reaches it.</summary>
    internal bool IsInView
    {
        get
        {
            if (!IsCurrent)
            {
                return false;
            }

            var middle = ViewMiddle();
            var page = View.PageSize;
            return middle.X >= 0 && middle.Y >= 0 && middle.X < page.Width && middle.Y < page.Height;
        }
    }

    private void ClickMiddle() => View.ClickPage(ViewMiddle());

    // Where it stands in the view's units: Chromium's pixels of the view, to units.
    private Rect ViewRect()
    {
        var rect = Page?.PageRect(Node) ?? Rect.Empty;
        var scale = View.PageScale;
        return scale > 0 ? new Rect(rect.X / scale, rect.Y / scale, rect.Width / scale, rect.Height / scale) : rect;
    }

    private Vector2<double> ViewMiddle()
    {
        var rect = ViewRect();
        return new Vector2<double>(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }
}
