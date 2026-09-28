namespace Adamantium.UI.Controls;

/// <summary>An opaque identity for a ribbon command with no <see cref="Ribbon.QuickAccessKeyProperty"/>, so the bar can
/// tell commands apart. Applications that need to recognize commands set their own key.</summary>
public sealed class RibbonCommandIdentity
{
    public override string ToString() => $"command #{GetHashCode():x}";
}
