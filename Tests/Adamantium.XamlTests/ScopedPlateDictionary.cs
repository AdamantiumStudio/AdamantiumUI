using Adamantium.UI.Core.MarkupExtensions;
using Adamantium.UI.Core.Resources;

namespace Adamantium.XamlTests;

/// <summary>A dictionary that brings styles only through what it includes: <see cref="ScopedPlateStyles"/>.</summary>
public sealed class ScopedPlateDictionary : ResourceDictionary
{
    public ScopedPlateDictionary()
    {
        Includes.Add(new ResourceLink { Source = typeof(ScopedPlateStyles) });
    }
}
