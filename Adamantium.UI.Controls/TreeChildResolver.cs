using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Adamantium.UI.Controls;

// Resolves a node's children without a container by reflecting the template's ItemsSource path; compiled getters are
// cached per (type, segment).
internal static class TreeChildResolver
{
    private static readonly ConcurrentDictionary<(Type, string), Func<object, object>> Getters = new();

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
    /// data grid's cell needs, and by the same compiled-accessor route the tree uses for children: a cell may be asked
    /// for on every row in the window, on every pass.</summary>
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

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo> Props = new();

    /// <summary>A writer for any <paramref name="path"/>, converting to the member's type; returns false when it cannot
    /// write.</summary>
    public static Func<object, object, bool> SetterForPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return static (_, _) => false;
        }

        var segments = path.Split('.');
        return (node, value) =>
        {
            var target = node;
            for (var i = 0; i < segments.Length - 1 && target != null; i++)
            {
                target = Getter(target.GetType(), segments[i])?.Invoke(target);
            }

            if (target == null) return false;

            var prop = Props.GetOrAdd((target.GetType(), segments[^1]), static k => k.Item1.GetProperty(k.Item2));
            if (prop is not { CanWrite: true }) return false;

            try
            {
                var wanted = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                var converted = value == null || wanted.IsInstanceOfType(value)
                    ? value
                    : Convert.ChangeType(value, wanted, Core.Localization.Languages.Culture);
                prop.SetValue(target, converted);
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
            var target = node;
            for (var i = 0; i < segments.Length - 1 && target != null; i++)
            {
                target = Getter(target.GetType(), segments[i])?.Invoke(target);
            }

            if (target == null)
            {
                return;
            }

            var prop = Props.GetOrAdd((target.GetType(), segments[^1]), static k => k.Item1.GetProperty(k.Item2));
            if (prop is { CanWrite: true })
            {
                prop.SetValue(target, value);
            }
        };
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

            current = Getter(current.GetType(), segment)?.Invoke(current);
        }

        return current;
    }

    private static Func<object, object> Getter(Type type, string name)
        => Getters.GetOrAdd((type, name), static key =>
        {
            var prop = key.Item1.GetProperty(key.Item2);
            if (prop is not { CanRead: true })
            {
                return null;
            }

            var o = Expression.Parameter(typeof(object), "o");
            var body = Expression.Convert(Expression.Property(Expression.Convert(o, key.Item1), prop), typeof(object));
            return Expression.Lambda<Func<object, object>>(body, o).Compile();
        });
}
