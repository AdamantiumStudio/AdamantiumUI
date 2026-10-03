using System;
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

    public double SampleNumber => 1234567.891;

    public DateTime SampleDate => new(2026, 10, 3, 17, 45, 0);

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
