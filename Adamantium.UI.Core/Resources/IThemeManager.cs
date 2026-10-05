using System;

namespace Adamantium.UI.Core.Resources;

public interface IThemeManager : IThemeEngine
{
    ITheme CurrentTheme { get; }

    /// <summary>Every theme registered with <see cref="AddTheme"/>, in registration order. Part of the contract because
    /// offering the user a CHOICE of theme is the ordinary reason to have more than one, and a chooser cannot be
    /// written against "the current one" alone.</summary>
    System.Collections.Generic.IReadOnlyList<ITheme> Themes { get; }

    /// <summary>Raised SYNCHRONOUSLY at the start of <see cref="SetTheme"/>, before anything is re-styled - the moment to
    /// put a busy overlay up. <see cref="IsThemeChanging"/> is already true here.</summary>
    event EventHandler<ThemeChangedEventArgs> ThemeChanging;

    /// <summary>Raised when the swap has fully SETTLED - not when <see cref="SetTheme"/> returns. Applying a theme
    /// re-styles, re-templates and re-lays-out the tree over several layout passes, so the work is far from done when the
    /// call returns; this fires on the first pass that finds nothing left to do, in every window the swap touched.</summary>
    event EventHandler<ThemeChangedEventArgs> ThemeChanged;

    /// <summary>True between <see cref="ThemeChanging"/> and <see cref="ThemeChanged"/> - i.e. while the swap's cascade is
    /// still draining. What a busy indicator is driven by.</summary>
    bool IsThemeChanging { get; }

    /// <summary>A MINIMUM time the busy overlay stays up once a swap begins (seconds). 0 (default) = finish the moment the
    /// cascade drains; set higher to keep the swap loader on screen long enough to see it spin.</summary>
    double MinSwapSeconds { get; set; }

    void AddTheme(string name, ITheme theme);

    /// <summary>Adds the style set <typeparamref name="T"/> to every theme, the ones added later included: how a library
    /// gives its own controls their look whichever theme is current. Each theme gets an instance of its own, and its
    /// styles count as the theme's, so the application's styles still outrank them. Adding a set twice adds it once.
    /// Elements styled before the call keep their styles, so call it before they exist - a control's static
    /// constructor is the place, through <see cref="FundamentalUIComponent.AddStyleSetToThemes{T}"/>.</summary>
    void AddStyleSet<T>() where T : StyleSet, new();

    /// <summary>Adds the style set of <paramref name="styleSetType"/> to every theme, as <see cref="AddStyleSet{T}"/> does,
    /// for a type known only at run time - an application blueprint's style includes.</summary>
    void AddStyleSet(Type styleSetType);

    void RemoveTheme(string name);

    void SetTheme(ITheme theme);

    /// <summary>Switches the theme's variant (e.g. light to dark) by recoloring palette brushes in place, without
    /// restyling; false if the theme lacks it. Raises no swap events.</summary>
    bool SetVariant(ThemeVariant variant);

    void ApplyTheme(ITheme theme, IFundamentalUIComponent component);
    
    void ApplyTheme(string name, IFundamentalUIComponent component);

    void RemoveStyles(IFundamentalUIComponent component);

    Style[] FindStylesForComponent(IFundamentalUIComponent component);

    ITheme this[string name] { get; }
    
    ITheme this[int index] { get; }
}