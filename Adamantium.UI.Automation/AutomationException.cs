using System;

namespace Adamantium.UI.Automation;

/// <summary>An automation command could not be carried out: nothing matched, the element cannot do that, or the
/// application did not answer. The message says which, and where.</summary>
public sealed class AutomationException : Exception
{
    public AutomationException(string message) : base(message)
    {
    }
}
