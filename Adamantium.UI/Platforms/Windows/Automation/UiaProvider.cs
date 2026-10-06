using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Platforms.Windows.Automation;

internal abstract class UiaProvider : IRawElementProviderSimple2, IRawElementProviderFragment, IUiaInvokeProvider,
    IUiaToggleProvider, IUiaValueProvider, IUiaRangeValueProvider, IUiaSelectionProvider, IUiaSelectionItemProvider,
    IUiaExpandCollapseProvider, IUiaScrollProvider, IUiaScrollItemProvider, IUiaGridProvider, IUiaGridItemProvider,
    IUiaTableProvider, IUiaTableItemProvider, IUiaWindowProvider, IUiaTextProvider, IUiaTransform2Provider,
    IUiaDockProvider
{
    private const string FrameworkId = "Adamantium";
    private const double ScrollStep = 10;
    private const double ZoomStep = 10;
    private const double LargeZoomStep = 100;

    protected UiaProvider(UiaBridge bridge)
    {
        Bridge = bridge;
    }

    public UiaBridge Bridge { get; }

    public abstract AutomationPeer FindPeer();

    public UiaProviderOptions GetProviderOptions() => UiaProviderOptions.ServerSideProvider;

    public nint GetPatternProvider(int patternId)
    {
        var pattern = PatternOf(patternId);
        return pattern != null && Bridge.Run(() => Peer().GetPattern(pattern.Value) != null) ? UiaBridge.ComPointer(this) : 0;
    }

    public UiaVariant GetPropertyValue(int propertyId) => Bridge.Run(() => Property(Peer(), propertyId));

    public abstract IRawElementProviderSimple GetHostRawElementProvider();

    public IRawElementProviderFragment Navigate(UiaNavigateDirection direction) => Bridge.Run(() =>
    {
        var peer = Peer();
        switch (direction)
        {
            case UiaNavigateDirection.Parent:
                return ReferenceEquals(this, Bridge.Root) ? null : (IRawElementProviderFragment)Bridge.ProviderFor(peer.GetParent() ?? Bridge.WindowPeer());
            case UiaNavigateDirection.FirstChild:
                return Bridge.ProviderFor(peer.GetChildren().FirstOrDefault());
            case UiaNavigateDirection.LastChild:
                return Bridge.ProviderFor(peer.GetChildren().LastOrDefault());
            case UiaNavigateDirection.NextSibling:
                return Sibling(peer, 1);
            case UiaNavigateDirection.PreviousSibling:
                return Sibling(peer, -1);
            default:
                return null;
        }
    });

    public abstract nint GetRuntimeId();

    public abstract UiaRect GetBoundingRectangle();

    public nint GetEmbeddedFragmentRoots() => 0;

    public void SetFocus() => Bridge.Run(() => Peer().SetFocus());

    public void ShowContextMenu() => Bridge.Run(() => Peer().ShowContextMenu());

    public IRawElementProviderFragmentRoot GetFragmentRoot() => Bridge.Root;

    public void Invoke() => Bridge.Run(() => Pattern<IInvokeProvider>(PatternId.Invoke).Invoke());

    public void Toggle() => Bridge.Run(() => Pattern<IToggleProvider>(PatternId.Toggle).Toggle());

    public UiaToggleState GetToggleState() => Bridge.Run(() => ToUia(Pattern<IToggleProvider>(PatternId.Toggle).ToggleState));

    public void SetValue(string value) => Bridge.Run(() => Pattern<IValueProvider>(PatternId.Value).SetValue(value));

    string IUiaValueProvider.GetValue() => Bridge.Run(() => Pattern<IValueProvider>(PatternId.Value).Value);

    bool IUiaValueProvider.GetIsReadOnly() => Bridge.Run(() => Pattern<IValueProvider>(PatternId.Value).IsReadOnly);

    public void SetValue(double value) => Bridge.Run(() => Pattern<IRangeValueProvider>(PatternId.RangeValue).SetValue(value));

    double IUiaRangeValueProvider.GetValue() => Bridge.Run(() => Range().Value);

    bool IUiaRangeValueProvider.GetIsReadOnly() => Bridge.Run(() => Range().IsReadOnly);

    public double GetMaximum() => Bridge.Run(() => Range().Maximum);

    public double GetMinimum() => Bridge.Run(() => Range().Minimum);

    public double GetLargeChange() => Bridge.Run(() => Range().LargeChange);

    public double GetSmallChange() => Bridge.Run(() => Range().SmallChange);

    public nint GetSelection() => Bridge.Run(() =>
        UiaSafeArray.Of(Pattern<ISelectionProvider>(PatternId.Selection).GetSelection().Select(Bridge.ProviderFor).ToList()));

    public bool GetCanSelectMultiple() => Bridge.Run(() => Pattern<ISelectionProvider>(PatternId.Selection).CanSelectMultiple);

    public bool GetIsSelectionRequired() => Bridge.Run(() => Pattern<ISelectionProvider>(PatternId.Selection).IsSelectionRequired);

    public void Select() => Bridge.Run(() => Pattern<ISelectionItemProvider>(PatternId.SelectionItem).Select());

    public void AddToSelection() => Bridge.Run(() => Pattern<ISelectionItemProvider>(PatternId.SelectionItem).AddToSelection());

    public void RemoveFromSelection() =>
        Bridge.Run(() => Pattern<ISelectionItemProvider>(PatternId.SelectionItem).RemoveFromSelection());

    public bool GetIsSelected() => Bridge.Run(() => Pattern<ISelectionItemProvider>(PatternId.SelectionItem).IsSelected);

    public IRawElementProviderSimple GetSelectionContainer() =>
        Bridge.Run(() => Bridge.ProviderFor(Pattern<ISelectionItemProvider>(PatternId.SelectionItem).SelectionContainer));

    public void Expand() => Bridge.Run(() => Pattern<IExpandCollapseProvider>(PatternId.ExpandCollapse).Expand());

    public void Collapse() => Bridge.Run(() => Pattern<IExpandCollapseProvider>(PatternId.ExpandCollapse).Collapse());

    public UiaExpandCollapseState GetExpandCollapseState() =>
        Bridge.Run(() => ToUia(Pattern<IExpandCollapseProvider>(PatternId.ExpandCollapse).ExpandCollapseState));

    public void Scroll(UiaScrollAmount horizontalAmount, UiaScrollAmount verticalAmount) => Bridge.Run(() =>
    {
        var scroll = Pattern<IScrollProvider>(PatternId.Scroll);
        scroll.SetScrollPercent(
            Stepped(scroll.HorizontalScrollPercent, scroll.HorizontalViewSize, horizontalAmount),
            Stepped(scroll.VerticalScrollPercent, scroll.VerticalViewSize, verticalAmount));
    });

    public void SetScrollPercent(double horizontalPercent, double verticalPercent) =>
        Bridge.Run(() => Pattern<IScrollProvider>(PatternId.Scroll).SetScrollPercent(horizontalPercent, verticalPercent));

    public double GetHorizontalScrollPercent() => Bridge.Run(() => Pattern<IScrollProvider>(PatternId.Scroll).HorizontalScrollPercent);

    public double GetVerticalScrollPercent() => Bridge.Run(() => Pattern<IScrollProvider>(PatternId.Scroll).VerticalScrollPercent);

    public double GetHorizontalViewSize() => Bridge.Run(() => Pattern<IScrollProvider>(PatternId.Scroll).HorizontalViewSize);

    public double GetVerticalViewSize() => Bridge.Run(() => Pattern<IScrollProvider>(PatternId.Scroll).VerticalViewSize);

    public bool GetHorizontallyScrollable() => Bridge.Run(() => Pattern<IScrollProvider>(PatternId.Scroll).HorizontallyScrollable);

    public bool GetVerticallyScrollable() => Bridge.Run(() => Pattern<IScrollProvider>(PatternId.Scroll).VerticallyScrollable);

    public void ScrollIntoView() => Bridge.Run(() => Pattern<IScrollItemProvider>(PatternId.ScrollItem).ScrollIntoView());

    public IRawElementProviderSimple GetItem(int row, int column) =>
        Bridge.Run(() => Bridge.ProviderFor(Pattern<IGridProvider>(PatternId.Grid).GetItem(row, column)));

    public int GetRowCount() => Bridge.Run(() => Pattern<IGridProvider>(PatternId.Grid).RowCount);

    public int GetColumnCount() => Bridge.Run(() => Pattern<IGridProvider>(PatternId.Grid).ColumnCount);

    public int GetRow() => Bridge.Run(() => Pattern<IGridItemProvider>(PatternId.GridItem).Row);

    public int GetColumn() => Bridge.Run(() => Pattern<IGridItemProvider>(PatternId.GridItem).Column);

    public int GetRowSpan() => Bridge.Run(() => Pattern<IGridItemProvider>(PatternId.GridItem).RowSpan);

    public int GetColumnSpan() => Bridge.Run(() => Pattern<IGridItemProvider>(PatternId.GridItem).ColumnSpan);

    public nint GetRowHeaderItems() => Bridge.Run(() =>
        UiaSafeArray.Of(Pattern<ITableItemProvider>(PatternId.TableItem).GetRowHeaders().Select(Bridge.ProviderFor).ToList()));

    public nint GetColumnHeaderItems() => Bridge.Run(() =>
        UiaSafeArray.Of(Pattern<ITableItemProvider>(PatternId.TableItem).GetColumnHeaders().Select(Bridge.ProviderFor).ToList()));

    public IRawElementProviderSimple GetContainingGrid() =>
        Bridge.Run(() => Bridge.ProviderFor(Pattern<IGridItemProvider>(PatternId.GridItem).ContainingGrid));

    public nint GetRowHeaders() => 0;

    public nint GetColumnHeaders() => Bridge.Run(() =>
        UiaSafeArray.Of(Pattern<ITableProvider>(PatternId.Table).GetColumnHeaders().Select(Bridge.ProviderFor).ToList()));

    public UiaRowOrColumnMajor GetRowOrColumnMajor() => UiaRowOrColumnMajor.RowMajor;

    public void SetVisualState(UiaWindowVisualState state) =>
        Bridge.Run(() => Pattern<IWindowProvider>(PatternId.Window).SetVisualState(FromUia(state)));

    public void Close() => Bridge.Run(() => Pattern<IWindowProvider>(PatternId.Window).Close());

    public bool WaitForInputIdle(int milliseconds) => true;

    public bool GetCanMaximize() => Bridge.Run(() => Pattern<IWindowProvider>(PatternId.Window).CanMaximize);

    public bool GetCanMinimize() => Bridge.Run(() => Pattern<IWindowProvider>(PatternId.Window).CanMinimize);

    public bool GetIsModal() => false;

    public UiaWindowVisualState GetWindowVisualState() =>
        Bridge.Run(() => ToUia(Pattern<IWindowProvider>(PatternId.Window).VisualState));

    public UiaWindowInteractionState GetWindowInteractionState() => UiaWindowInteractionState.ReadyForUserInteraction;

    public bool GetIsTopmost() => false;

    nint IUiaTextProvider.GetSelection() => Bridge.Run(() =>
    {
        var text = Pattern<ITextProvider>(PatternId.Text);
        return UiaSafeArray.Of(new[] { new UiaTextRange(this, text.SelectionStart, text.SelectionStart + text.SelectionLength) });
    });

    public nint GetVisibleRanges() => Bridge.Run(() =>
        UiaSafeArray.Of(new[] { new UiaTextRange(this, 0, Pattern<ITextProvider>(PatternId.Text).Text.Length) }));

    public IUiaTextRangeProvider RangeFromChild(IRawElementProviderSimple child) => null;

    public IUiaTextRangeProvider RangeFromPoint(UiaPoint point) => Bridge.Run(() =>
    {
        var index = Pattern<ITextProvider>(PatternId.Text).IndexAt(new PixelPoint(point.X, point.Y));
        return (IUiaTextRangeProvider)new UiaTextRange(this, index, index);
    });

    public IUiaTextRangeProvider GetDocumentRange() =>
        Bridge.Run(() => (IUiaTextRangeProvider)new UiaTextRange(this, 0, Pattern<ITextProvider>(PatternId.Text).Text.Length));

    public UiaSupportedTextSelection GetSupportedTextSelection() => UiaSupportedTextSelection.Single;

    public void Move(double x, double y) => Bridge.Run(() => Transform().Move(x, y));

    public void Resize(double width, double height) => Bridge.Run(() => Transform().Resize(width, height));

    public void Rotate(double degrees) =>
        throw new COMException("Nothing here is rotated.", unchecked((int)0x80131509));

    public bool GetCanMove() => Bridge.Run(() => Transform().CanMove);

    public bool GetCanResize() => Bridge.Run(() => Transform().CanResize);

    public bool GetCanRotate() => false;

    public void Zoom(double zoom) => Bridge.Run(() => Transform().Zoom(zoom));

    public bool GetCanZoom() => Bridge.Run(() => Transform().CanZoom);

    public double GetZoomLevel() => Bridge.Run(() => Transform().ZoomLevel);

    public double GetZoomMinimum() => Bridge.Run(() => Transform().ZoomMinimum);

    public double GetZoomMaximum() => Bridge.Run(() => Transform().ZoomMaximum);

    public void SetDockPosition(UiaDockPosition dockPosition) =>
        Bridge.Run(() => Pattern<IDockProvider>(PatternId.Dock).SetDockPosition((DockPosition)(int)dockPosition));

    public UiaDockPosition GetDockPosition() => Bridge.Run(() => (UiaDockPosition)(int)Pattern<IDockProvider>(PatternId.Dock).DockPosition);

    public void ZoomByUnit(UiaZoomUnit zoomUnit) => Bridge.Run(() =>
    {
        var transform = Transform();
        var step = zoomUnit switch
        {
            UiaZoomUnit.LargeIncrement => LargeZoomStep,
            UiaZoomUnit.LargeDecrement => -LargeZoomStep,
            UiaZoomUnit.SmallIncrement => ZoomStep,
            UiaZoomUnit.SmallDecrement => -ZoomStep,
            _ => 0
        };
        transform.Zoom(Math.Clamp(transform.ZoomLevel + step, transform.ZoomMinimum, transform.ZoomMaximum));
    });

    public static UiaVariant Property(AutomationPeer peer, int propertyId) => propertyId switch
    {
        UiaIds.ControlTypeProperty => UiaVariant.From(ControlTypeOf(peer.ControlType)),
        UiaIds.NameProperty => UiaVariant.From(peer.Name),
        UiaIds.AutomationIdProperty => UiaVariant.From(peer.AutomationId),
        UiaIds.ClassNameProperty => UiaVariant.From(peer.ClassName),
        UiaIds.HelpTextProperty => Text(peer.HelpText),
        UiaIds.AccessKeyProperty => Text(peer.AccessKey),
        UiaIds.IsEnabledProperty => UiaVariant.From(peer.IsEnabled),
        UiaIds.IsOffscreenProperty => UiaVariant.From(peer.IsOffscreen),
        UiaIds.HasKeyboardFocusProperty => UiaVariant.From(peer.HasKeyboardFocus),
        UiaIds.IsKeyboardFocusableProperty => UiaVariant.From(peer.IsKeyboardFocusable),
        UiaIds.IsControlElementProperty or UiaIds.IsContentElementProperty => UiaVariant.From(true),
        UiaIds.FrameworkIdProperty => UiaVariant.From(FrameworkId),
        _ => UiaVariant.Empty
    };

    public static int ControlTypeOf(AutomationControlType type) => type switch
    {
        AutomationControlType.Button => UiaIds.ButtonControlType,
        AutomationControlType.Calendar => UiaIds.CalendarControlType,
        AutomationControlType.CheckBox => UiaIds.CheckBoxControlType,
        AutomationControlType.ComboBox => UiaIds.ComboBoxControlType,
        AutomationControlType.DataGrid => UiaIds.DataGridControlType,
        AutomationControlType.DataItem => UiaIds.DataItemControlType,
        AutomationControlType.Document => UiaIds.DocumentControlType,
        AutomationControlType.Edit => UiaIds.EditControlType,
        AutomationControlType.Group => UiaIds.GroupControlType,
        AutomationControlType.Header => UiaIds.HeaderControlType,
        AutomationControlType.HeaderItem => UiaIds.HeaderItemControlType,
        AutomationControlType.Hyperlink => UiaIds.HyperlinkControlType,
        AutomationControlType.Image => UiaIds.ImageControlType,
        AutomationControlType.List => UiaIds.ListControlType,
        AutomationControlType.ListItem => UiaIds.ListItemControlType,
        AutomationControlType.Menu => UiaIds.MenuControlType,
        AutomationControlType.MenuBar => UiaIds.MenuBarControlType,
        AutomationControlType.MenuItem => UiaIds.MenuItemControlType,
        AutomationControlType.Pane => UiaIds.PaneControlType,
        AutomationControlType.ProgressBar => UiaIds.ProgressBarControlType,
        AutomationControlType.RadioButton => UiaIds.RadioButtonControlType,
        AutomationControlType.ScrollBar => UiaIds.ScrollBarControlType,
        AutomationControlType.Separator => UiaIds.SeparatorControlType,
        AutomationControlType.Slider => UiaIds.SliderControlType,
        AutomationControlType.Spinner => UiaIds.SpinnerControlType,
        AutomationControlType.SplitButton => UiaIds.SplitButtonControlType,
        AutomationControlType.StatusBar => UiaIds.StatusBarControlType,
        AutomationControlType.Tab => UiaIds.TabControlType,
        AutomationControlType.TabItem => UiaIds.TabItemControlType,
        AutomationControlType.Table => UiaIds.TableControlType,
        AutomationControlType.Text => UiaIds.TextControlType,
        AutomationControlType.Thumb => UiaIds.ThumbControlType,
        AutomationControlType.TitleBar => UiaIds.TitleBarControlType,
        AutomationControlType.ToolBar => UiaIds.ToolBarControlType,
        AutomationControlType.ToolTip => UiaIds.ToolTipControlType,
        AutomationControlType.Tree => UiaIds.TreeControlType,
        AutomationControlType.TreeItem => UiaIds.TreeItemControlType,
        AutomationControlType.Window => UiaIds.WindowControlType,
        _ => UiaIds.CustomControlType
    };

    public static UiaToggleState ToUia(ToggleState state) => state switch
    {
        ToggleState.On => UiaToggleState.On,
        ToggleState.Indeterminate => UiaToggleState.Indeterminate,
        _ => UiaToggleState.Off
    };

    public static UiaExpandCollapseState ToUia(ExpandCollapseState state) => state switch
    {
        ExpandCollapseState.Expanded => UiaExpandCollapseState.Expanded,
        ExpandCollapseState.LeafNode => UiaExpandCollapseState.LeafNode,
        _ => UiaExpandCollapseState.Collapsed
    };

    public static UiaWindowVisualState ToUia(WindowState state) => state switch
    {
        WindowState.Maximized => UiaWindowVisualState.Maximized,
        WindowState.Minimized => UiaWindowVisualState.Minimized,
        _ => UiaWindowVisualState.Normal
    };

    protected AutomationPeer Peer() =>
        FindPeer() ?? throw new COMException("The element is gone.", UiaBridge.ElementNotAvailable);

    protected static UiaRect ToRect(Rect bounds) => bounds.IsEmpty
        ? default
        : new UiaRect { Left = bounds.X, Top = bounds.Y, Width = bounds.Width, Height = bounds.Height };

    private T Pattern<T>(PatternId pattern) where T : class =>
        Peer().GetPattern(pattern) as T ?? throw new COMException("The element no longer supports the pattern.", UiaBridge.ElementNotAvailable);

    private IRangeValueProvider Range() => Pattern<IRangeValueProvider>(PatternId.RangeValue);

    private ITransformProvider Transform() => Pattern<ITransformProvider>(PatternId.Transform);

    private IRawElementProviderFragment Sibling(AutomationPeer peer, int step)
    {
        if (ReferenceEquals(this, Bridge.Root))
        {
            return null;
        }

        var siblings = (peer.GetParent() ?? Bridge.WindowPeer())?.GetChildren() ?? [];
        for (var i = 0; i < siblings.Count; i++)
        {
            if (ReferenceEquals(siblings[i], peer))
            {
                var next = i + step;
                return next >= 0 && next < siblings.Count ? Bridge.ProviderFor(siblings[next]) : null;
            }
        }

        return null;
    }

    private static PatternId? PatternOf(int patternId) => patternId switch
    {
        UiaIds.InvokePattern => PatternId.Invoke,
        UiaIds.TogglePattern => PatternId.Toggle,
        UiaIds.ValuePattern => PatternId.Value,
        UiaIds.RangeValuePattern => PatternId.RangeValue,
        UiaIds.SelectionPattern => PatternId.Selection,
        UiaIds.SelectionItemPattern => PatternId.SelectionItem,
        UiaIds.ExpandCollapsePattern => PatternId.ExpandCollapse,
        UiaIds.ScrollPattern => PatternId.Scroll,
        UiaIds.ScrollItemPattern => PatternId.ScrollItem,
        UiaIds.GridPattern => PatternId.Grid,
        UiaIds.GridItemPattern => PatternId.GridItem,
        UiaIds.TablePattern => PatternId.Table,
        UiaIds.WindowPattern => PatternId.Window,
        UiaIds.TextPattern => PatternId.Text,
        UiaIds.TableItemPattern => PatternId.TableItem,
        UiaIds.TransformPattern or UiaIds.Transform2Pattern => PatternId.Transform,
        UiaIds.DockPattern => PatternId.Dock,
        _ => null
    };

    private static UiaVariant Text(string text) => string.IsNullOrEmpty(text) ? UiaVariant.Empty : UiaVariant.From(text);

    private static WindowState FromUia(UiaWindowVisualState state) => state switch
    {
        UiaWindowVisualState.Maximized => WindowState.Maximized,
        UiaWindowVisualState.Minimized => WindowState.Minimized,
        _ => WindowState.Normal
    };

    private static double Stepped(double percent, double viewSize, UiaScrollAmount amount)
    {
        if (percent < 0)
        {
            return IScrollProvider.NoScroll;
        }

        var step = amount switch
        {
            UiaScrollAmount.LargeIncrement => viewSize,
            UiaScrollAmount.LargeDecrement => -viewSize,
            UiaScrollAmount.SmallIncrement => ScrollStep,
            UiaScrollAmount.SmallDecrement => -ScrollStep,
            _ => 0
        };
        return Math.Clamp(percent + step, 0, 100);
    }
}
