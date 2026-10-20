using System.Runtime.CompilerServices;
using Adamantium.MVVM;

namespace Adamantium.UI.Core.Diagnostics;

#pragma warning disable CA2255
internal static class PropertyAccessTrace
{
    [ModuleInitializer]
    internal static void Watch()
    {
        PropertyAccessors.ReachedThroughReflection += type =>
            Serilog.Log.Logger.Information(
                "Bindings reach {Type} through reflection: no generated description of it", type.FullName);
    }
}
