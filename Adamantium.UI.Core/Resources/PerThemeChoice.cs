namespace Adamantium.UI.Core.Resources;

/// <summary>A type added to every theme - a style set or a resource dictionary - with the ones particular themes take
/// instead: <see cref="ByTheme"/> names a theme by its type, or a type it derives from, and the type it takes.</summary>
internal sealed class PerThemeChoice
{
    public PerThemeChoice(Type @default, IReadOnlyDictionary<Type, Type> byTheme)
    {
        Default = @default;
        ByTheme = byTheme;
    }

    /// <summary>What every theme not named in <see cref="ByTheme"/> takes.</summary>
    public Type Default { get; }

    /// <summary>What the themes named take instead; null when none is.</summary>
    public IReadOnlyDictionary<Type, Type> ByTheme { get; }

    /// <summary>The type <paramref name="theme"/> takes: the one given for its type or the nearest type it derives from,
    /// else <see cref="Default"/>.</summary>
    public Type For(ITheme theme)
    {
        if (ByTheme != null)
        {
            for (var type = theme.GetType(); type != null; type = type.BaseType)
            {
                if (ByTheme.TryGetValue(type, out var chosen))
                {
                    return chosen;
                }
            }
        }

        return Default;
    }

    /// <summary>Whether <paramref name="other"/> gives the same type to every theme named.</summary>
    public bool SameAs(PerThemeChoice other)
    {
        var count = ByTheme?.Count ?? 0;
        if (count != (other.ByTheme?.Count ?? 0))
        {
            return false;
        }

        if (count == 0)
        {
            return true;
        }

        foreach (var pair in ByTheme)
        {
            if (!other.ByTheme.TryGetValue(pair.Key, out var chosen) || chosen != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Throws when a type given is not a <paramref name="baseType"/> made without arguments, naming
    /// <paramref name="defaultParameter"/> or <paramref name="byThemeParameter"/>, whichever it came in.</summary>
    /// <exception cref="ArgumentException">A type given is not one.</exception>
    public void Check(Type baseType, string what, string defaultParameter, string byThemeParameter)
    {
        if (!IsMadeWithoutArguments(Default, baseType))
        {
            throw new ArgumentException($"{Default?.Name ?? "Nothing"} is not a {what} made without arguments.",
                defaultParameter);
        }

        if (ByTheme == null)
        {
            return;
        }

        foreach (var pair in ByTheme)
        {
            if (!IsMadeWithoutArguments(pair.Value, baseType))
            {
                throw new ArgumentException(
                    $"The {what} for {pair.Key.Name}, {pair.Value?.Name ?? "nothing"}, is not a {what} made without arguments.",
                    byThemeParameter);
            }
        }
    }

    private static bool IsMadeWithoutArguments(Type type, Type baseType) =>
        type != null && baseType.IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null;
}
