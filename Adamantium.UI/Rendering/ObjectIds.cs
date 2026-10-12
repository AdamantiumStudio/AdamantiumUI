using System.Runtime.CompilerServices;
using System.Threading;

namespace Adamantium.UI.Rendering;

internal static class ObjectIds
{
    private static readonly ConditionalWeakTable<object, StrongBox<long>> Ids = new();
    private static long _next;

    /// <summary>A number no other live or past object gets; 0 for null.</summary>
    public static long Of(object value) => value == null
        ? 0
        : Ids.GetValue(value, static _ => new StrongBox<long>(Interlocked.Increment(ref _next))).Value;
}
