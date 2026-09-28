using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Sandbox.Behaviors;

/// <summary>On click, switches the theme variant between light and dark, recoloring palette brushes without
/// restyling.</summary>
public class ToggleThemeBehavior : Behavior<Button>
{
    private Button _button;

    protected override void OnAttached(Button button)
    {
        _button = button;
        button.Click += OnClick;
    }

    protected override void OnDetached(Button button)
    {
        button.Click -= OnClick;
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        var themeManager = UIAppContext.Current?.ThemeManager;
        if (themeManager?.CurrentTheme is not Adamantium.UI.Core.Resources.Theme theme) return;

        var next = theme.CurrentVariant == Adamantium.UI.Core.Resources.ThemeVariant.Dark
            ? Adamantium.UI.Core.Resources.ThemeVariant.Light
            : Adamantium.UI.Core.Resources.ThemeVariant.Dark;

        themeManager.SetVariant(next);
    }
}
