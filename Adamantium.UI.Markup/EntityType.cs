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
        Control,

        /// <summary>What an application starts with. A project holds one; the build makes it a class, names it to the
        /// assembly and writes the entry point that runs the application.</summary>
        ApplicationBlueprint,

        /// <summary>Data templates picked by the type of the item - a class deriving from <c>DataTemplateSet</c>, which
        /// markup places by name wherever a template selector goes.</summary>
        DataTemplateSet
    }
}
