using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Mathematics;

namespace Adamantium.UI.Platforms;

public interface IApplicationPlatform
{
    void Run(CancellationToken token);

    bool IsOnUIThread { get; }

    void Signal();

    event Action Signaled;

    /// <summary>Waits on the application's own thread, while it starts, for <paramref name="task"/> to end, however it
    /// ends. Where the splash window lives on that thread, what happened to it is answered meanwhile, and before a task
    /// already over.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="token"/> was canceled first.</exception>
    void Wait(Task task, CancellationToken token) => Task.WaitAny([task], token);

    /// <summary>The native handle of the OS-topmost window at a physical desktop point, or zero when unknown;
    /// click-through windows are skipped.</summary>
    IntPtr WindowFromScreenPoint(Adamantium.UI.Core.PixelPoint point);
}