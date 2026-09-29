using System.Runtime.CompilerServices;

namespace Adamantium.UI.Core.Templates;

public class NameScope : INameScope
{
    private static readonly ConditionalWeakTable<object, NameScope> Scopes = new();

    private readonly Dictionary<string, object> nameScope;

    public NameScope()
    {
        nameScope = new Dictionary<string, object>();
    }

    /// <summary>Records <paramref name="element"/> under <paramref name="name"/> in the name scope of the markup
    /// <paramref name="root"/> it was declared in: what <c>{Binding ElementName=…}</c> finds it by, whether or not it
    /// has been laid out yet. A name declared again replaces the earlier entry.</summary>
    public static void Register(object root, string name, object element)
    {
        if (root == null || string.IsNullOrEmpty(name))
        {
            return;
        }

        var scope = Scopes.GetValue(root, static _ => new NameScope());
        lock (scope)
        {
            scope.Unregister(name);
            scope.RegisterName(name, element);
        }
    }

    /// <summary>The element <paramref name="root"/>'s markup declared under <paramref name="name"/>, or null.</summary>
    public static object Find(object root, string name)
    {
        if (root == null || !Scopes.TryGetValue(root, out var scope))
        {
            return null;
        }

        lock (scope)
        {
            return scope.Find(name);
        }
    }

    public void RegisterName(string name, object component)
    {
        if (string.IsNullOrEmpty(name)) return;

        if (nameScope.TryGetValue(name, out var existingComponent))
        {
            if (component != existingComponent)
                throw new ArgumentException($"Component with name {name} already registered");
        }
        else
        {
            nameScope[name] = component;
        }

    }

    public void Unregister(string name)
    {
        if (string.IsNullOrEmpty(name)) return;

        nameScope.Remove(name);
    }

    public object Find(string name)
    {
        nameScope.TryGetValue(name, out var component);
        return component;
    }
}
