namespace Adamantium.UI.Core;

/// <summary>Marks a type, or a property, that markup writes as a path to a file with one of <see cref="Extensions"/>:
/// completion offers only such files there.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class MarkupFileAttribute : Attribute
{
    public MarkupFileAttribute(params string[] extensions)
    {
        Extensions = extensions;
    }

    public IReadOnlyList<string> Extensions { get; }
}
