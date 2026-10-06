namespace Adamantium.UI.Core.Automation;

/// <summary>Describes an element to automation - what it is, what it is called, where it stands and what can be done
/// with it - so a test, an agent or an accessibility client can find and drive it without knowing its class. A peer is
/// made only when automation asks for it.</summary>
public abstract class AutomationPeer
{
    private static int _lastRuntimeId;

    protected AutomationPeer()
    {
        RuntimeId = Interlocked.Increment(ref _lastRuntimeId);
    }

    /// <summary>Unique for the life of the process: how a driver names an element it has already found.</summary>
    public int RuntimeId { get; }

    public abstract AutomationControlType ControlType { get; }

    /// <summary>What a person sees or hears it called; follows the application's language.</summary>
    public abstract string Name { get; }

    /// <summary>The stable identifier a test finds it by; never translated.</summary>
    public abstract string AutomationId { get; }

    public abstract string HelpText { get; }

    /// <summary>The keys that reach the element from the keyboard, such as a ribbon command's key tip; empty when none.</summary>
    public virtual string AccessKey => string.Empty;

    /// <summary>The class that draws the element.</summary>
    public abstract string ClassName { get; }

    /// <summary>Where the element is on the screen, in physical pixels.</summary>
    public abstract Rect BoundingRectangle { get; }

    public abstract bool IsEnabled { get; }

    /// <summary>Not visible on the screen: collapsed or hidden, under such an ancestor, out of a window, or of no size.</summary>
    public abstract bool IsOffscreen { get; }

    public abstract bool HasKeyboardFocus { get; }

    public abstract bool IsKeyboardFocusable { get; }

    public abstract IReadOnlyList<AutomationPeer> GetChildren();

    public abstract AutomationPeer GetParent();

    public abstract void SetFocus();

    /// <summary>Opens the element's context menu as the menu key would - its own, or the nearest one above it - with the
    /// keyboard inside. Throws when there is none.</summary>
    public virtual void ShowContextMenu() =>
        throw new InvalidOperationException($"{ControlType} \"{Name}\" has no context menu.");

    /// <summary>The object that provides <paramref name="pattern"/>, or null when the element has no such capability. By
    /// default the peer itself, when it implements the pattern's interface.</summary>
    public virtual object GetPattern(PatternId pattern) => pattern switch
    {
        PatternId.Invoke => this as IInvokeProvider,
        PatternId.Toggle => this as IToggleProvider,
        PatternId.Value => this as IValueProvider,
        PatternId.Selection => this as ISelectionProvider,
        PatternId.SelectionItem => this as ISelectionItemProvider,
        PatternId.ExpandCollapse => this as IExpandCollapseProvider,
        PatternId.ScrollItem => this as IScrollItemProvider,
        PatternId.RangeValue => this as IRangeValueProvider,
        PatternId.Scroll => this as IScrollProvider,
        PatternId.Window => this as IWindowProvider,
        PatternId.Grid => this as IGridProvider,
        PatternId.GridItem => this as IGridItemProvider,
        PatternId.Table => this as ITableProvider,
        PatternId.Text => this as ITextProvider,
        PatternId.TableItem => this as ITableItemProvider,
        PatternId.Transform => this as ITransformProvider,
        PatternId.Connection => this as IConnectionProvider,
        PatternId.Dock => this as IDockProvider,
        PatternId.Pan => this as IPanProvider,
        _ => null
    };
}
