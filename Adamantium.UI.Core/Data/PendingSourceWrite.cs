using Adamantium.MVVM;

namespace Adamantium.UI.Core.Data;

/// <summary>A value a binding hands to its source's thread, with what it writes through as the binding stood when the
/// value was written: the UI's thread may point the binding elsewhere before the source's thread runs it.</summary>
internal sealed class PendingSourceWrite
{
    public PendingSourceWrite(object root, PropertyAccessor source, PropertyAccessor leaf, string[] segments, object value)
    {
        Root = root;
        Source = source;
        Leaf = leaf;
        Segments = segments;
        Value = value;
    }

    /// <summary>The object the path starts at - or, for a plain path, the object whose property is written.</summary>
    public object Root { get; }

    /// <summary>The property written, for a path that does not end inside a struct.</summary>
    public PropertyAccessor Source { get; }

    /// <summary>The member of the struct the path ends in; null for a plain path.</summary>
    public PropertyAccessor Leaf { get; }

    public string[] Segments { get; }

    public object Value { get; }
}
