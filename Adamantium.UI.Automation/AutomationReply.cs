using System.Collections.Generic;

namespace Adamantium.UI.Automation;

/// <summary>The application's answer to an <see cref="AutomationRequest"/>.</summary>
public sealed class AutomationReply
{
    public bool Ok { get; set; }

    /// <summary>Why the command failed, when it did.</summary>
    public string Error { get; set; }

    public List<ElementInfo> Elements { get; set; }

    public string Text { get; set; }

    public static AutomationReply Done() => new() { Ok = true };

    public static AutomationReply Failed(string error) => new() { Error = error };
}
