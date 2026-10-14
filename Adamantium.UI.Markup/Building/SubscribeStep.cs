namespace Adamantium.UI.Markup.Building;

/// <summary>Subscribes a method of the instance being built to an event.</summary>
public sealed class SubscribeStep(BuildValue target, string eventName, string handler) : BuildStep
{
    /// <summary>The object raising the event; null for the instance being built, written without <c>this.</c>.</summary>
    public BuildValue Target { get; } = target;

    /// <summary>The event's name.</summary>
    public string EventName { get; } = eventName;

    /// <summary>The name of the method that handles it.</summary>
    public string Handler { get; } = handler;
}
