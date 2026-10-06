using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Automation;

/// <summary>A handle on an element: the selector path that finds it, looked up afresh on every use, so it works the
/// same in the process and through a pipe and never keeps a control alive.</summary>
public sealed class AutomationElement
{
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WaitStep = TimeSpan.FromMilliseconds(20);

    private readonly AutomationSession _session;
    private readonly IReadOnlyList<By> _path;

    internal AutomationElement(AutomationSession session, IReadOnlyList<By> path)
    {
        _session = session;
        _path = path;
    }

    /// <summary>The selector path that finds this element.</summary>
    public string Selector => By.FormatPath(_path);

    /// <summary>The element <paramref name="by"/> finds somewhere below this one.</summary>
    public AutomationElement Find(By by) => new(_session, [.. _path, by]);

    /// <summary>The element <paramref name="by"/> finds among this one's children only.</summary>
    public AutomationElement Child(By by) => new(_session, [.. _path, by.AsChild()]);

    /// <summary>The <paramref name="index"/>-th of the elements this path finds, from 0; -1 for the last.</summary>
    public AutomationElement At(int index) => new(_session, [.. _path.Take(_path.Count - 1), _path[^1].At(index)]);

    /// <summary>The element that holds this one.</summary>
    public AutomationElement Parent() => new(_session, [.. _path, By.Parent]);

    /// <summary>The sibling after this one: the next item of a list, the next button of a row.</summary>
    public AutomationElement Next() => new(_session, [.. _path, By.Next]);

    /// <summary>The sibling before this one.</summary>
    public AutomationElement Previous() => new(_session, [.. _path, By.Previous]);

    public async Task<ElementInfo> GetAsync() => (await RunAsync(AutomationCommand.Get)).Elements[0];

    public async Task<string> NameAsync() => (await GetAsync()).Name;

    /// <summary>The named properties with where each value comes from - every property something has set when none is
    /// named - and the element's bindings, layout and parents.</summary>
    public async Task<ElementDetails> InspectAsync(params string[] properties) =>
        (await _session.RunAsync(new AutomationRequest
        {
            Command = AutomationCommand.Inspect,
            Target = Selector,
            Properties = properties
        })).Details;

    /// <summary>The visual tree under the element with every node's layout, as text; <paramref name="depth"/> 0 for all.</summary>
    public async Task<string> VisualAsync(int depth = 0) =>
        (await _session.RunAsync(new AutomationRequest { Command = AutomationCommand.Visual, Target = Selector, Depth = depth })).Text;

    public async Task<bool> ExistsAsync() => (await RunAsync(AutomationCommand.Find)).Elements.Count > 0;

    /// <summary>Waits until the element is there, for <paramref name="timeout"/> at most.</summary>
    public async Task<ElementInfo> WaitForAsync(TimeSpan? timeout = null) =>
        (await _session.RunAsync(new AutomationRequest
        {
            Command = AutomationCommand.WaitFor,
            Target = Selector,
            TimeoutMs = (int)(timeout?.TotalMilliseconds ?? 0)
        })).Elements[0];

    /// <summary>Waits until the element is there and <paramref name="condition"/> holds for it - its value became 40, it
    /// turned on - checking each time the application settles, for <paramref name="timeout"/> at most (ten seconds).</summary>
    /// <exception cref="AutomationException">It did not come to that in time; the message says how it was last seen.</exception>
    public async Task<ElementInfo> WaitUntilAsync(Func<ElementInfo, bool> condition, TimeSpan? timeout = null)
    {
        var limit = timeout ?? DefaultWait;
        var clock = Stopwatch.StartNew();
        ElementInfo last = null;
        while (true)
        {
            var found = await _session.SendAsync(new AutomationRequest { Command = AutomationCommand.Find, Target = Selector });
            last = found.Ok && found.Elements.Count > 0 ? found.Elements[0] : null;
            if (last != null && condition(last))
            {
                return last;
            }

            if (clock.Elapsed > limit)
            {
                throw new AutomationException($"'{Selector}' did not come to what was waited for within " +
                                              $"{limit.TotalSeconds:0.#} s; last seen: {(object)last ?? "not there"}.");
            }

            await _session.WaitForIdleAsync();
            await Task.Delay(WaitStep);
        }
    }

    /// <summary>A picture of the element, drawn by the application's renderer and written to <paramref name="path"/> as
    /// PNG - to look at, never to compare. Not in a headless session, which draws nothing.</summary>
    public async Task<string> ShotAsync(string path) => (await RunAsync(AutomationCommand.Shot, path)).Text;

    public Task InvokeAsync() => RunAsync(AutomationCommand.Invoke);

    public Task ToggleAsync() => RunAsync(AutomationCommand.Toggle);

    /// <summary>Makes it the one selected item of its container.</summary>
    public Task SelectAsync() => RunAsync(AutomationCommand.Select);

    /// <summary>Adds it to what its container has selected, leaving the rest - where the container selects many.</summary>
    public Task AddToSelectionAsync() => RunAsync(AutomationCommand.AddToSelection);

    /// <summary>Takes it out of what its container has selected, leaving the rest.</summary>
    public Task RemoveFromSelectionAsync() => RunAsync(AutomationCommand.RemoveFromSelection);

    /// <summary>Writes its text, or its number in the invariant culture.</summary>
    public Task SetValueAsync(string value) => RunAsync(AutomationCommand.SetValue, value);

    /// <summary>Scrolls it to the given percents; null leaves an axis where it is.</summary>
    public Task ScrollToAsync(double? horizontal, double? vertical) =>
        RunAsync(AutomationCommand.Scroll, $"{Percent(horizontal)},{Percent(vertical)}");

    /// <summary>Minimizes, maximizes or restores it, as the title bar's buttons do.</summary>
    public Task SetWindowStateAsync(WindowState state) => RunAsync(AutomationCommand.SetWindowState, state.ToString());

    /// <summary>Closes it, when it is a window.</summary>
    public Task CloseAsync() => RunAsync(AutomationCommand.Close);

    /// <summary>Opens what it holds: a drop-down's list, a submenu, a branch.</summary>
    public Task ExpandAsync() => RunAsync(AutomationCommand.Expand);

    public Task CollapseAsync() => RunAsync(AutomationCommand.Collapse);

    /// <summary>Scrolls its list until it is in view, making its element if it had none.</summary>
    public Task ScrollIntoViewAsync() => RunAsync(AutomationCommand.ScrollIntoView);

    /// <summary>Moves it by an offset of its own units, without input: a splitter along its axis, a node across its
    /// canvas.</summary>
    public Task MoveByAsync(double dx, double dy) => RunAsync(AutomationCommand.Move, FormattableString.Invariant($"{dx},{dy}"));

    /// <summary>Resizes it to a size in its own units, without input.</summary>
    public Task ResizeAsync(double width, double height) =>
        RunAsync(AutomationCommand.Resize, FormattableString.Invariant($"{width},{height}"));

    /// <summary>Docks it - a pane, a panel - against an edge, into the documents (<see cref="DockPosition.Fill"/>) or out
    /// into a window of its own (<see cref="DockPosition.None"/>); beside <paramref name="beside"/>'s panel when given,
    /// or into it as a tab for Fill.</summary>
    public Task DockAsync(DockPosition position, AutomationElement beside = null) =>
        _session.RunAsync(new AutomationRequest
        {
            Command = AutomationCommand.Dock,
            Target = Selector,
            Value = position.ToString(),
            Properties = beside == null ? null : [beside.Selector]
        });

    /// <summary>Joins it - a socket of a node - to <paramref name="other"/>, as a wire pulled by hand would.</summary>
    public Task ConnectAsync(AutomationElement other) => RunAsync(AutomationCommand.Connect, other.Selector);

    /// <summary>Parts it from <paramref name="other"/>, or from every socket it is joined to when that is null.</summary>
    public Task DisconnectAsync(AutomationElement other = null) => RunAsync(AutomationCommand.Disconnect, other?.Selector);

    /// <summary>Zooms it to <paramref name="percent"/>; 100 shows the content at its own size.</summary>
    public Task ZoomAsync(double percent) => RunAsync(AutomationCommand.Zoom, percent.ToString(CultureInfo.InvariantCulture));

    /// <summary>Drags what it - a view of a plane, a canvas - shows by an offset of its own units, without input.</summary>
    public Task PanAsync(double dx, double dy) => RunAsync(AutomationCommand.Pan, FormattableString.Invariant($"{dx},{dy}"));

    /// <summary>Opens its context menu - its own, or the nearest one above it - as the menu key would, without the mouse.</summary>
    public Task ShowContextMenuAsync() => RunAsync(AutomationCommand.ShowContextMenu);

    /// <summary>Carries it onto <paramref name="target"/> and lets it go there, as a hand would: over the target's middle,
    /// or just before or after it - a row among rows, a column among columns.</summary>
    public Task DropOntoAsync(AutomationElement target, DropSide side = DropSide.Onto) =>
        _session.RunAsync(new AutomationRequest
        {
            Command = AutomationCommand.DropOnto,
            Target = Selector,
            Value = target.Selector,
            Properties = side == DropSide.Onto ? null : [side.ToString()]
        });

    /// <summary>Moves the pointer over its middle, by input simulated inside the application.</summary>
    public Task HoverAsync() => RunAsync(AutomationCommand.Hover);

    /// <summary>A left-button drag across it from one point to another, in its own units, by input simulated inside the
    /// application.</summary>
    public Task DragAsync(double fromX, double fromY, double toX, double toY) =>
        RunAsync(AutomationCommand.Drag, FormattableString.Invariant($"{fromX},{fromY} {toX},{toY}"));

    /// <summary>A right click in its middle, by input simulated inside the application.</summary>
    public Task RightClickAsync() => RunAsync(AutomationCommand.RightClick);

    /// <summary>A left click in its middle, by input simulated inside the application; refused if something covers it.</summary>
    public Task ClickAsync() => RunAsync(AutomationCommand.Click);

    /// <summary>Presses <paramref name="keys"/> - chords apart by spaces, <c>Alt</c>, <c>Ctrl+S</c>, <c>Alt H</c> - in
    /// its window, focusing it first if it takes the keyboard; by input simulated inside the application.</summary>
    public Task PressKeysAsync(string keys) => RunAsync(AutomationCommand.Key, keys);

    /// <summary>Focuses it and types <paramref name="text"/>, by input simulated inside the application.</summary>
    public Task TypeAsync(string text) => RunAsync(AutomationCommand.Type, text);

    public override string ToString() => Selector;

    private static string Percent(double? percent) => percent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private Task<AutomationReply> RunAsync(AutomationCommand command, string value = null) =>
        _session.RunAsync(new AutomationRequest { Command = command, Target = Selector, Value = value });
}
