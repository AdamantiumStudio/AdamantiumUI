namespace Adamantium.UI.Core.Resources;

/// <summary>A setter value built per target (<c>x:Shared="False"</c>), for values that cannot be shared, such as a
/// <c>ContextMenu</c> or a <c>Transform</c>.</summary>
public sealed class PerTargetValue
{
    private readonly Func<object> _factory;

    public PerTargetValue(Func<object> factory)
    {
        _factory = factory;
    }

    /// <summary>Builds a value for one target. Called once per element the setter applies to.</summary>
    public object Create() => _factory?.Invoke();
}
