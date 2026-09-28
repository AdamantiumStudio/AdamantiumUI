using System.Linq;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>The "add to quick-access bar" row, placeable in an author's own command menu. Runs the inherited
/// <see cref="Ribbon.AddToQuickAccessCommandProperty"/> of the command it opened on.</summary>
public class RibbonQuickAccessMenuItem : MenuItem
{
    /// <summary>Whether the command this was opened on is in the bar already - what the theme reads to say "remove"
    /// rather than "add".</summary>
    public static readonly AdamantiumProperty IsInQuickAccessProperty = AdamantiumProperty.Register(
        nameof(IsInQuickAccess), typeof(bool), typeof(RibbonQuickAccessMenuItem),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    public bool IsInQuickAccess
    {
        get => GetValue<bool>(IsInQuickAccessProperty);
        private set => SetValue(IsInQuickAccessProperty, value);
    }

    /// <summary>The command whose context menu this row is in. Found up the LOGICAL tree, not the visual one: a menu's
    /// rows are rendered in its popup, on a subtree detached from everything, while logically they are exactly where
    /// their author wrote them - inside the menu.</summary>
    public IUIComponent Target => this.GetLogicalAncestors().OfType<ContextMenu>().FirstOrDefault()?.PlacementTarget;

    public RibbonQuickAccessMenuItem()
    {
        Click += OnPicked;
    }

    /// <summary>The menu is built once and opened many times, so the row is asked again on every showing - which is what
    /// an attach now is for popup content (see <see cref="PopupLayer"/>).</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
    }

    /// <summary>...and again the moment the row learns WHERE it is. On the very first showing the visual attach above
    /// runs before the row has a logical parent, so it cannot yet see the menu it is in - <see cref="Target"/> comes back
    /// null and the row states the wrong thing until the menu is opened a second time.</summary>
    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Refresh();
    }

    /// <summary>The menu is built once and opened many times, and what it should say depends on the command it was
    /// opened on THIS time.</summary>
    public void Refresh()
    {
        var target = Target;
        IsInQuickAccess = target != null && Ribbon.IsShownInQuickAccess(target);
        IsEnabled = target != null && Ribbon.GetCanAddToQuickAccess(target);
    }

    private void OnPicked(object sender, RoutedEventArgs e)
    {
        var target = Target;
        if (target == null) return;

        Ribbon.RequestQuickAccess(target, !Ribbon.IsShownInQuickAccess(target));
        Refresh();
    }
}
