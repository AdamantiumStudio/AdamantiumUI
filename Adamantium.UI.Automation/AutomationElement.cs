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

    public async Task<bool> ExistsAsync() => (await RunAsync(AutomationCommand.Find)).Elements.Count > 0;

    /// <summary>Waits until the element is there, for <paramref name="timeout"/> at most.</summary>
    public async Task<ElementInfo> WaitForAsync(TimeSpan? timeout = null) =>
        (await _session.RunAsync(new AutomationRequest
        {
            Command = AutomationCommand.WaitFor,
            Target = Selector,
            TimeoutMs = (int)(timeout?.TotalMilliseconds ?? 0)
        })).Elements[0];

    public Task InvokeAsync() => RunAsync(AutomationCommand.Invoke);

    public Task ToggleAsync() => RunAsync(AutomationCommand.Toggle);

    public Task SelectAsync() => RunAsync(AutomationCommand.Select);

    public Task SetValueAsync(string value) => RunAsync(AutomationCommand.SetValue, value);

    /// <summary>A left click in its middle, by input simulated inside the application; refused if something covers it.</summary>
    public Task ClickAsync() => RunAsync(AutomationCommand.Click);

    /// <summary>Focuses it and types <paramref name="text"/>, by input simulated inside the application.</summary>
    public Task TypeAsync(string text) => RunAsync(AutomationCommand.Type, text);

    public override string ToString() => Selector;

    private Task<AutomationReply> RunAsync(AutomationCommand command, string value = null) =>
        _session.RunAsync(new AutomationRequest { Command = command, Target = Selector, Value = value });
}
