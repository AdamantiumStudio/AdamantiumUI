using Adamantium.UI.Markup.AST;

namespace Adamantium.UI.Markup.CodeGeneration;

// Picks nodes (and properties) out of a list without an iterator, a closure or a list when there is nothing to pick.
internal static class NodeScan
{
    public static T First<T>(List<IAumlAstNode> nodes, Func<T, bool> match = null) where T : class
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is T node && (match == null || match(node))) return node;
        }

        return null;
    }

    public static T First<T>(List<IAumlAstValueNode> nodes, Func<T, bool> match = null) where T : class
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is T node && (match == null || match(node))) return node;
        }

        return null;
    }

    public static IResolvedProperty Named(IReadOnlyList<IResolvedProperty> properties, string name)
    {
        if (properties == null)
        {
            return null;
        }

        for (var i = 0; i < properties.Count; i++)
        {
            if (properties[i].Name == name) return properties[i];
        }

        return null;
    }

    public static IReadOnlyList<T> All<T>(List<IAumlAstValueNode> nodes) where T : class
    {
        List<T> found = null;
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is T node) (found ??= []).Add(node);
        }

        return found ?? (IReadOnlyList<T>)Array.Empty<T>();
    }
}
