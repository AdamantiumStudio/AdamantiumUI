namespace Adamantium.UI.Markup.Building;

/// <summary>A method call and what it returns.</summary>
public sealed class CallValue(BuildValue target, string method, IReadOnlyList<TypeValue> typeArguments, params BuildValue[] arguments)
    : BuildValue
{
    /// <summary>A call without type arguments.</summary>
    public CallValue(BuildValue target, string method, params BuildValue[] arguments)
        : this(target, method, [], arguments)
    {
    }

    /// <summary>The object or type the method is called on; null for the instance being built, written without
    /// <c>this.</c>.</summary>
    public BuildValue Target { get; } = target;

    /// <summary>The method's name.</summary>
    public string Method { get; } = method;

    /// <summary>The generic method's type arguments.</summary>
    public IReadOnlyList<TypeValue> TypeArguments { get; } = typeArguments;

    /// <summary>The arguments, in order.</summary>
    public IReadOnlyList<BuildValue> Arguments { get; } = arguments;
}
