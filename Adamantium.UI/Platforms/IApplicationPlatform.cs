using System;
using System.Threading;
using Adamantium.Mathematics;

namespace Adamantium.UI.Platforms;

public interface IApplicationPlatform
{
    void Run(CancellationToken token);

    bool IsOnUIThread { get; }

    void Signal();

    event Action Signaled;

    /// <summary>The native handle of the OS-topmost window at a physical desktop point, or zero when unknown;
    /// click-through windows are skipped.</summary>
    IntPtr WindowFromScreenPoint(Adamantium.UI.Core.PixelPoint point);
}