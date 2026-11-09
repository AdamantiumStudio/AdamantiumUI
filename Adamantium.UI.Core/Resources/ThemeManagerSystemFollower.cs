namespace Adamantium.UI.Core.Resources;

/// <summary>Carries a change of the OS appearance to a <see cref="ThemeManager"/> without keeping it alive: the event is
/// static and outlives every manager, so this holds its manager weakly and lets the event go once it is collected.</summary>
internal sealed class ThemeManagerSystemFollower
{
    private readonly WeakReference<ThemeManager> _manager;

    public ThemeManagerSystemFollower(ThemeManager manager)
    {
        _manager = new WeakReference<ThemeManager>(manager);
        SystemAppearance.Changed += OnChanged;
    }

    private void OnChanged(object sender, EventArgs e)
    {
        if (_manager.TryGetTarget(out var manager))
        {
            manager.OnSystemAppearanceChanged();
        }
        else
        {
            SystemAppearance.Changed -= OnChanged;
        }
    }
}
