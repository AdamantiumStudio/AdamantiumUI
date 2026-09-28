using System;

namespace Adamantium.UI.Core.Diagnostics;

/// <summary>Opt-in binding trace, like <see cref="LayoutTrace"/>: set <see cref="Enabled"/> and <see cref="Sink"/> to see
/// failures such as an <c>{Ancestor}</c> that found nothing. Off by default.</summary>
public static class BindingTrace
{
    public static bool Enabled;
    public static Action<string> Sink;

    public static void Log(string message)
    {
        if (Enabled) Sink?.Invoke(message);
    }
}
