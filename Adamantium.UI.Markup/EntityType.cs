namespace Adamantium.UI.Markup
{
    public enum EntityType
    {
        Unknown,
        Window,
        View,
        Theme,
        UIApplication,
        ResourceDictionary,
        StyleSet,
        ThemeVariant,

        /// <summary>Any other UI element - a ribbon tab, a group, a panel. A file with it at the root is a class deriving
        /// from it, which other markup places by name.</summary>
        Control
    }
}
