using System;
using System.Collections;
using Adamantium.MVVM;

namespace Adamantium.UI.Controls;

// Resolves a node's children without a container by reading the template's ItemsSource path through PropertyAccessors.
internal static class TreeChildResolver
{
    /// <summary>A getter that reads <paramref name="path"/> off a node and returns it as an <see cref="IEnumerable"/>
    /// (null when the path is empty, unresolved, or the value isn't enumerable).</summary>
    public static Func<object, IEnumerable> ForPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return static _ => null;
        }

        var segments = path.Split('.');
        return node => Resolve(node, segments) as IEnumerable;
    }

    /// <summary>A getter that reads <paramref name="path"/> off a node and returns the VALUE, whatever it is - what a
    /// data grid's cell needs, and by the same accessor route the tree uses for children: a cell may be asked for on
    /// every row in the window, on every pass.</summary>
    public static Func<object, object> ForValuePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return static node => node;
        }

        var segments = path.Split('.');
        return node => Resolve(node, segments);
    }

    /// <summary>A getter that reads a <see cref="bool"/> <paramref name="path"/> off a node (e.g. the node's own
    /// <c>IsExpanded</c>, so the flattener can RESTORE a node's expansion when the tree is rebuilt - after a tab switch the
    /// view is recreated but the view-model, and its expanded state, persist). Always false for an empty/unresolved path.</summary>
    public static Func<object, bool> ForBoolPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return static _ => false;
        }

        var segments = path.Split('.');
        return node => Resolve(node, segments) is true;
    }

    /// <summary>A writer for any <paramref name="path"/>, converting to the member's type; returns false when it cannot
    /// write - the member has no public setter, or will not take the value.</summary>
    public static Func<object, object, bool> SetterForPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return static (_, _) => false;
        }

        var segments = path.Split('.');
        return (node, value) =>
        {
            if (Leaf(node, segments) is not (var target, var accessor) || !accessor.CanWrite)
            {
                return false;
            }

            try
            {
                var wanted = Nullable.GetUnderlyingType(accessor.ValueType) ?? accessor.ValueType;
                var converted = value == null || wanted.IsInstanceOfType(value)
                    ? value
                    : Convert.ChangeType(value, wanted, Core.Localization.Languages.Culture);
                accessor.Set(target, converted);
                return true;
            }
            catch (Exception)
            {
                // A value the member will not take is a refusal to commit, not a crash mid-edit.
                return false;
            }
        };
    }

    /// <summary>A writer for a <see cref="bool"/> <paramref name="path"/> such as <c>IsSelected</c>, so selection persists
    /// on off-screen nodes; no-op for an unresolved path.</summary>
    public static Action<object, bool> SetterForBoolPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return static (_, _) => { };
        }

        var segments = path.Split('.');
        return (node, value) =>
        {
            if (Leaf(node, segments) is (var target, var accessor) && accessor.CanWrite)
            {
                accessor.Set(target, value);
            }
        };
    }

    private static (object Target, PropertyAccessor Accessor)? Leaf(object node, string[] segments)
    {
        var target = node;
        for (var i = 0; i < segments.Length - 1 && target != null; i++)
        {
            target = PropertyAccessors.Find(target.GetType(), segments[i])?.Get?.Invoke(target);
        }

        if (target == null || PropertyAccessors.Find(target.GetType(), segments[^1]) is not { } accessor)
        {
            return null;
        }

        return (target, accessor);
    }

    private static object Resolve(object node, string[] segments)
    {
        var current = node;
        foreach (var segment in segments)
        {
            if (current == null)
            {
                return null;
            }

            current = PropertyAccessors.Find(current.GetType(), segment)?.Get?.Invoke(current);
        }

        return current;
    }
}
