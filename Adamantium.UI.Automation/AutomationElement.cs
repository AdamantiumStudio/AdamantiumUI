using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Adamantium.UI.Automation;

/// <summary>A handle on an element: the selector path that finds it, looked up afresh on every use, so it works the
/// same in the process and through a pipe and never keeps a control alive.</summary>
public sealed class AutomationElement
{
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

    /// <summary>A picture of the element, drawn by the application's renderer and written to <paramref name="path"/> as
    /// PNG - to look at, never to compare. Not in a headless session, which draws nothing.</summary>
    public async Task<string> ShotAsync(string path) => (await RunAsync(AutomationCommand.Shot, path)).Text;

    public Task InvokeAsync() => RunAsync(AutomationCommand.Invoke);

    public Task ToggleAsync() => RunAsync(AutomationCommand.Toggle);

    public Task SelectAsync() => RunAsync(AutomationCommand.Select);

    public Task SetValueAsync(string value) => RunAsync(AutomationCommand.SetValue, value);

    /// <summary>Opens what it holds: a drop-down's list, a submenu, a branch.</summary>
    public Task ExpandAsync() => RunAsync(AutomationCommand.Expand);

    public Task CollapseAsync() => RunAsync(AutomationCommand.Collapse);

    /// <summary>Scrolls its list until it is in view, making its element if it had none.</summary>
    public Task ScrollIntoViewAsync() => RunAsync(AutomationCommand.ScrollIntoView);

    /// <summary>Moves the pointer over its middle, by input simulated inside the application.</summary>
    public Task HoverAsync() => RunAsync(AutomationCommand.Hover);

    /// <summary>A right click in its middle, by input simulated inside the application.</summary>
    public Task RightClickAsync() => RunAsync(AutomationCommand.RightClick);

    /// <summary>A left click in its middle, by input simulated inside the application; refused if something covers it.</summary>
    public Task ClickAsync() => RunAsync(AutomationCommand.Click);

    /// <summary>Focuses it and types <paramref name="text"/>, by input simulated inside the application.</summary>
    public Task TypeAsync(string text) => RunAsync(AutomationCommand.Type, text);

    public override string ToString() => Selector;

    private Task<AutomationReply> RunAsync(AutomationCommand command, string value = null) =>
        _session.RunAsync(new AutomationRequest { Command = command, Target = Selector, Value = value });
}
