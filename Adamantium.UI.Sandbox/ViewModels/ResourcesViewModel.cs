using System.Collections.Generic;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Resources tab: live {ObservableResource} versus resolve-once {ResourceReference} for a theme palette key and
/// an inline local resource, plus a theme-scope stand showing several variants.</summary>
[ViewModel]
public partial class ResourcesViewModel : TabPageViewModel
{
    public ResourcesViewModel() : base("Resources") { }

    /// <summary>The scope stand's theme, <c>Fluent</c>, unlike the window's <c>FluentDark</c>. It must be a complete theme,
    /// since a scope replaces the app theme. Resolved lazily, after themes load.</summary>
    public ITheme LocalTheme => UIAppContext.Current?.ThemeManager?["Fluent"];

    /// <summary>Every theme the application has registered, by name - what the stand's theme chooser lists.
    /// <para>Read from the manager each time rather than captured: themes are registered while the application comes
    /// up, and a list captured in the constructor would be the empty one.</para></summary>
    public System.Collections.Generic.IReadOnlyList<string> AvailableThemes
    {
        get
        {
            var themes = UIAppContext.Current?.ThemeManager?.Themes;
            if (themes == null) return [];

            var names = new List<string>(themes.Count);
            foreach (var theme in themes) names.Add(theme.Name);
            return names;
        }
    }

    /// <summary>Which theme the chooser shows. Defaults to the one the application is ON, resolved on FIRST READ rather
    /// than in the constructor: a view-model is built while the application is still coming up, when the theme manager
    /// has no themes yet, so a value taken there would be null for good and the chooser would open on nothing.</summary>
    public string SelectedThemeName
    {
        get => _selectedThemeName ??= UIAppContext.Current?.ThemeManager?.CurrentTheme?.Name;
        set
        {
            if (_selectedThemeName == value) return;
            _selectedThemeName = value;
            RaisePropertyChanged(nameof(SelectedThemeName));
        }
    }

    private string _selectedThemeName;

    /// <summary>Move the whole application onto the chosen theme. A SWAP, not a variant switch: different style sets and
    /// different metrics, so every template is rebuilt - which is exactly what the stand exists to make visible next to
    /// the variant button, whose cost is a color write per palette key.</summary>
    [Command]
    private void SwitchTheme()
    {
        var manager = UIAppContext.Current?.ThemeManager;
        if (manager == null || string.IsNullOrEmpty(SelectedThemeName)) return;

        var theme = manager[SelectedThemeName];
        if (theme != null && !ReferenceEquals(theme, manager.CurrentTheme)) manager.SetTheme(theme);
    }

    /// <summary>The switchable pane's own light/dark, changed by its own checkbox and by nothing else - not by the
    /// application's theme buttons, and not by the OS.</summary>
    [Bindable] private bool _standIsDark;

    public ThemeVariant StandVariant => StandIsDark ? ThemeVariant.Dark : ThemeVariant.Light;

    partial void OnStandIsDarkChanged(bool value) => RaisePropertyChanged(nameof(StandVariant));

    /// <summary>Caption buttons on the LEFT - the macOS and Ubuntu convention. A platform theme normally states this
    /// once with a setter; the checkbox is here so the switch can be seen live, on a real window, without a rebuild.
    /// <para>Written to every OPEN window rather than to a theme resource: the side is a property of a window's chrome,
    /// and a demo that changed only the main one would leave the tear-off windows disagreeing with it.</para></summary>
    [Bindable] private bool _captionButtonsOnLeft;

    partial void OnCaptionButtonsOnLeftChanged(bool value)
    {
        var placement = value ? CaptionButtonPlacement.Left : CaptionButtonPlacement.Right;
        var windows = UIAppContext.Current?.Windows;
        if (windows == null) return;

        foreach (var window in windows)
        {
            if (window is WindowBase w) w.CaptionButtonPlacement = placement;
        }
    }
}
