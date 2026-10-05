namespace Adamantium.UI.Automation;

/// <summary>One live binding on an element.</summary>
public sealed class BindingInfo
{
    public string Property { get; set; }

    /// <summary>What kind of binding, and its path where it has one.</summary>
    public string Binding { get; set; }

    public string Status { get; set; }

    /// <summary>Why it does not work, when it does not.</summary>
    public string Failure { get; set; }
}
