using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Adorners;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Universes;
using NUnit.Framework;
using Rectangle = Adamantium.UI.Controls.Shapes.Rectangle;
using T = Adamantium.UI.Core.Automation.AutomationControlType;

namespace Adamantium.XamlTests;

/// <summary>
/// Every public element of the framework, made on its own, is what this table says it is to automation: a control type
/// of its own, or looked through - and then why. An element nobody decided about fails, and so does a Custom type with
/// no reason given. "OPEN" marks what is still to be done.
/// </summary>
[TestFixture]
public class AutomationAuditTests
{
    private const string Layout = "lays out or draws what it holds; the elements in it answer for themselves";

    private static readonly Dictionary<Type, (T? Type, string Reason)> Decided = new()
    {
        [typeof(BusyIndicator)] = (T.ProgressBar, null),
        [typeof(Button)] = (T.Button, null),
        [typeof(CheckBox)] = (T.CheckBox, null),
        [typeof(ColorPicker)] = (T.Custom, "UI Automation has no color type; the color is its value"),
        [typeof(ColorPickerButton)] = (T.Button, null),
        [typeof(ColorWheel)] = (T.Custom, "UI Automation has no color type; the color is its value"),
        [typeof(ContentControl)] = (T.Custom, "a bare content control says nothing of what it is; the controls on it do"),
        [typeof(ContentPresenter)] = (null, "presents another control's content"),
        [typeof(ContextMenu)] = (T.Menu, null),
        [typeof(Control)] = (T.Custom, "a bare control says nothing of what it is; the controls on it do"),
        [typeof(DataPager)] = (T.Group, null),
        [typeof(DragHandle)] = (T.Thumb, null),
        [typeof(DropDown)] = (T.ComboBox, null),
        [typeof(DropDownItem)] = (T.ListItem, null),
        [typeof(Expander)] = (T.Group, null),
        [typeof(FlipTile)] = (T.Button, null),
        [typeof(Image)] = (T.Image, null),
        [typeof(ItemsControl)] = (T.Group, null),
        [typeof(ItemsPresenter)] = (null, "presents another control's items"),
        [typeof(ListBox)] = (T.List, null),
        [typeof(ListBoxItem)] = (T.ListItem, null),
        [typeof(MenuScrollViewer)] = (T.Pane, null),
        [typeof(NumericUpDown)] = (T.Spinner, null),
        [typeof(OverlayWindow)] = (T.Window, null),
        [typeof(Popup)] = (null, "what it shows is a child of whatever opened it"),
        [typeof(ProgressBar)] = (T.ProgressBar, null),
        [typeof(PropertyGrid)] = (T.Pane, null),
        [typeof(PropertyRow)] = (T.DataItem, null),
        [typeof(PropertySection)] = (T.Group, null),
        [typeof(RadioButton)] = (T.RadioButton, null),
        [typeof(RangeSlider)] = (T.Slider, null),
        [typeof(ResizeGripper)] = (T.Thumb, null),
        [typeof(Ribbon)] = (T.Tab, null),
        [typeof(RibbonApplicationMenu)] = (T.Button, null),
        [typeof(RibbonApplicationMenuItem)] = (T.Button, null),
        [typeof(RibbonButton)] = (T.Button, null),
        [typeof(RibbonContextualLedge)] = (null, "a plate under contextual tabs; the tabs carry their context"),
        [typeof(RibbonDropDownButton)] = (T.Button, null),
        [typeof(RibbonGallery)] = (T.List, null),
        [typeof(RibbonGalleryItem)] = (T.ListItem, null),
        [typeof(RibbonGroup)] = (T.Group, null),
        [typeof(RibbonQuickAccess)] = (T.ToolBar, null),
        [typeof(RibbonQuickAccessMenuItem)] = (T.MenuItem, null),
        [typeof(RibbonRadioButton)] = (T.RadioButton, null),
        [typeof(RibbonSplitButton)] = (T.SplitButton, null),
        [typeof(RibbonTab)] = (T.Pane, null),
        [typeof(RibbonTabHeader)] = (T.TabItem, null),
        [typeof(RibbonTabSet)] = (null, "a piece of a module's view; its tabs join the ribbon's"),
        [typeof(RibbonToggleButton)] = (T.Button, null),
        [typeof(RingProgressBar)] = (T.ProgressBar, null),
        [typeof(ScrollContentPresenter)] = (null, "presents a scroll viewer's content"),
        [typeof(ScrollViewer)] = (T.Pane, null),
        [typeof(Separator)] = (T.Separator, null),
        [typeof(SlidePanel)] = (T.Pane, null),
        [typeof(Slider)] = (T.Slider, null),
        [typeof(TabControl)] = (T.Tab, null),
        [typeof(TabItem)] = (T.TabItem, null),
        [typeof(TabItemsControl)] = (T.Group, null),
        [typeof(TabStripScroller)] = (null, "the tab strip's arrows; selecting a tab brings it into the strip"),
        [typeof(TilesHost)] = (T.Group, null),
        [typeof(TitleBar)] = (T.TitleBar, null),
        [typeof(ToggleSwitch)] = (T.Button, null),
        [typeof(ToolTip)] = (T.ToolTip, null),
        [typeof(TreeView)] = (T.Tree, null),
        [typeof(TreeViewItem)] = (T.TreeItem, null),
        [typeof(View)] = (T.Pane, null),
        [typeof(Window)] = (T.Window, null),
        [typeof(VirtualWindow)] = (T.Window, null),
        [typeof(ZoomBox)] = (T.Pane, null),
        [typeof(UIComponent)] = (null, "a base: no role of its own"),
        [typeof(ObservableUIComponent)] = (null, "a base: no role of its own"),
        [typeof(MeasurableUIComponent)] = (null, "a base: no role of its own"),
        [typeof(InputUIComponent)] = (null, "a base: no role of its own"),
        [typeof(TemplatedUIComponent)] = (null, "a base: no role of its own"),
        [typeof(DataGridCell)] = (T.Text, null),
        [typeof(DataGridColumnChooser)] = (T.Group, null),
        [typeof(DataGridColumnHeader)] = (T.HeaderItem, null),
        [typeof(DataGridFilterView)] = (T.Group, null),
        [typeof(DataGridFooterCell)] = (T.Text, null),
        [typeof(DataGridGroupChip)] = (T.DataItem, null),
        [typeof(DataGridGroupHeader)] = (T.Text, null),
        [typeof(DataGridGroupPanel)] = (T.ToolBar, null),
        [typeof(DataGridRow)] = (T.DataItem, null),
        [typeof(DataGridRowDetailsToggle)] = (T.Button, null),
        [typeof(DataGridRowHeader)] = (T.HeaderItem, null),
        [typeof(DataGridSearchPanel)] = (T.Group, null),
        [typeof(DataGridSortChip)] = (T.DataItem, null),
        [typeof(DataGridSortPanel)] = (T.ToolBar, null),
        [typeof(TreeDataGrid)] = (T.DataGrid, null),
        [typeof(DockCompassWindow)] = (T.Window, null),
        [typeof(DockingWindow)] = (T.Window, null),
        [typeof(Pane)] = (T.TabItem, null),
        [typeof(PaneGroup)] = (T.Tab, null),
        [typeof(UnsavedQuestion)] = (T.Pane, null),
        [typeof(CanvasDrawLayer)] = (null, "draws ink and shapes; the canvas's inspector lists them"),
        [typeof(CanvasFrontLayer)] = (null, "draws over the plane; nothing on it is acted on"),
        [typeof(CanvasInspector)] = (T.Pane, null),
        [typeof(CanvasMiniMap)] = (T.Image, null),
        [typeof(CanvasNode)] = (T.DataItem, null),
        [typeof(CanvasNodePalette)] = (T.Pane, null),
        [typeof(CanvasNodeSocket)] = (T.Thumb, null),
        [typeof(CanvasPane)] = (T.Pane, null),
        [typeof(CanvasQuestion)] = (T.Pane, null),
        [typeof(CanvasSelectionBar)] = (T.ToolBar, null),
        [typeof(CanvasViewBar)] = (T.ToolBar, null),
        [typeof(InfiniteCanvas)] = (T.Pane, null),
        [typeof(GridSplitter)] = (T.Thumb, null),
        [typeof(PaneSplitter)] = (T.Thumb, null),
        [typeof(RenderTargetPanel)] = (null, "a surface the engine draws its scene into; the scene is not UI"),
        [typeof(MenuItem)] = (T.MenuItem, null),
        [typeof(RepeatButton)] = (T.Button, null),
        [typeof(ScrollBar)] = (T.ScrollBar, null),
        [typeof(ScrollBarPageButton)] = (null, "a part of a scroll bar, which scrolls by its value"),
        [typeof(Thumb)] = (T.Thumb, null),
        [typeof(ToggleButton)] = (T.Button, null),
        [typeof(FractalView)] = (T.Pane, null),
        [typeof(TextBlock)] = (T.Text, null),
        [typeof(TextBox)] = (T.Edit, null),
        [typeof(TextPresenter)] = (null, "draws a text box's text; the text box answers for it"),
    };

    /// <summary>Whole families looked through, so a new panel or shape needs no line of its own.</summary>
    private static readonly (Type Family, string Reason)[] LookedThrough =
    [
        (typeof(Panel), Layout),
        (typeof(Decorator), Layout),
        (typeof(Shape), "a drawing; found by its automation id when it is given one"),
        (typeof(Adorner), "drawn over another element for it; the element is what is acted on"),
    ];

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        var app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(app, null);
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, app);
    }

    private static IEnumerable<Type> Elements() =>
        new[] { typeof(Control).Assembly, typeof(VirtualWindow).Assembly }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !type.IsAbstract && !type.ContainsGenericParameters && typeof(UIComponent).IsAssignableFrom(type))
            .OrderBy(type => type.FullName);

    [Test]
    public void EveryElement_IsWhatTheTableSays()
    {
        var wrong = new List<string>();
        foreach (var type in Elements())
        {
            if (Decided.TryGetValue(type, out var decided))
            {
                if (decided.Type is null or T.Custom && string.IsNullOrWhiteSpace(decided.Reason))
                {
                    wrong.Add($"{type.Name}: {(decided.Type == null ? "looked through" : "Custom")} with no reason");
                }

                var actual = PeerTypeOf(type, out var made);
                if (made && actual != decided.Type)
                {
                    wrong.Add($"{type.Name}: expected {decided.Type?.ToString() ?? "no peer"}, is {actual?.ToString() ?? "no peer"}");
                }

                continue;
            }

            if (LookedThrough.Any(family => family.Family.IsAssignableFrom(type)))
            {
                var actual = PeerTypeOf(type, out var made);
                if (made && actual != null)
                {
                    wrong.Add($"{type.Name}: its family is looked through, yet it is {actual}; give it a line of the table");
                }

                continue;
            }

            wrong.Add($"{type.Name}: nobody decided what it is to automation");
        }

        Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
    }

    [Test]
    public void TheTable_NamesNoElementThatIsGone()
    {
        var elements = Elements().ToHashSet();
        Assert.That(Decided.Keys.Where(type => !elements.Contains(type)), Is.Empty);
    }

    private static T? PeerTypeOf(Type type, out bool made)
    {
        made = type.GetConstructor(Type.EmptyTypes) != null;
        return made ? ((UIComponent)Activator.CreateInstance(type)).GetAutomationPeer()?.ControlType : null;
    }
}
