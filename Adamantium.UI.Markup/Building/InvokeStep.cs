namespace Adamantium.UI.Markup.Building;

/// <summary>Calls a method for what it does; what it returns is dropped.</summary>
public sealed class InvokeStep(CallValue call) : BuildStep
{
    /// <summary>The call.</summary>
    public CallValue Call { get; } = call;
}
