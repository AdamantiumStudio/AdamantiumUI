using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Adamantium.UI.Core.Markup.MarkupHotReloadUpdateHandler))]

namespace Adamantium.UI.Core.Markup;

internal static class MarkupHotReloadUpdateHandler
{
    public static void UpdateApplication(Type[] updatedTypes)
    {
        _ = MarkupHotReload.CodeUpdatedAsync();
    }
}
