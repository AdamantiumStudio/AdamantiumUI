namespace Adamantium.UI.Automation;

/// <summary>One command to the application, the same whether it goes to an executor in the process or through the
/// agent's pipe.</summary>
public sealed class AutomationRequest
{
    public AutomationCommand Command { get; set; }

    /// <summary>The selector path of the element the command is about - see <see cref="By"/>.</summary>
    public string Target { get; set; }

    public string Value { get; set; }

    /// <summary>How deep <see cref="AutomationCommand.Tree"/> goes; 0 for all the way.</summary>
    public int Depth { get; set; }

    /// <summary>How long the command may take, waiting for the application included; 0 for the default.</summary>
    public int TimeoutMs { get; set; }
}
