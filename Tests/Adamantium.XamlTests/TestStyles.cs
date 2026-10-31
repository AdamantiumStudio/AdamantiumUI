using System;
using Adamantium.UI.Core.Resources;

namespace Adamantium.XamlTests;

/// <summary>Styles built in code for the tests of how styles are taken.</summary>
internal static class TestStyles
{
    /// <summary>A style for <paramref name="type"/> - of <paramref name="className"/>, when one is given - setting one
    /// property.</summary>
    public static Style Create(Type type, string property, object value, string className = null)
    {
        var style = new Style();
        style.Selector.Types.Add(type);
        if (className != null)
        {
            style.Selector.Classes.Add(className);
        }

        style.Setters.Add(new Setter(property, value));
        return style;
    }
}
