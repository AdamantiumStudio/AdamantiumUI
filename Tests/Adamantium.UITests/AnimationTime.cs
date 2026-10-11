using System.Diagnostics;
using System.Threading;
using Adamantium.UI.Core.Media.Animation;

namespace Adamantium.UITests;

internal static class AnimationTime
{
    public static void Pass(double seconds)
    {
        Compositor.StartPending();
        var until = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < until)
        {
            Thread.Sleep(5);
        }

        AnimationManager.Tick(seconds);
    }
}
