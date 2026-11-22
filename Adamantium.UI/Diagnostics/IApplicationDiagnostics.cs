using System;
using System.Threading.Tasks;
using Adamantium.UI.Rendering.Verification;

namespace Adamantium.UI.Diagnostics;

/// <summary>What automation and debugging read of a running application and the tools they attach to it - not for the
/// application's own work. Resolved from the application's container (<c>UIContext.Resolve</c>).</summary>
public interface IApplicationDiagnostics
{
    /// <summary>The pace the loop keeps now, in hertz.</summary>
    double LoopRate { get; }

    /// <summary>The frame verifier: off by default, switched on at run time to hunt a rendering bug.</summary>
    FrameVerification FrameVerification { get; }

    /// <summary>Raised on the loop thread at the end of every cycle of the loop.</summary>
    event EventHandler CycleFinished;

    /// <summary>What kept the application from going idle the last time it was waited for; null once it was idle.</summary>
    string IdleBlocker { get; }

    /// <summary>Completes once the application is idle: the loop has nothing left to do - no posted work, nothing to lay
    /// out or draw, no animation that finishes on its own, no picture still being read from its file - and the frame
    /// showing that state has been drawn. Endless animations (a caret, a spinner) do not count, nor does work running
    /// off the loop until it posts its result.</summary>
    Task WaitForIdleAsync();

    /// <summary>Starts <paramref name="tool"/> on the application, on the calling thread; it stays attached until
    /// detached or until the application shuts down. Attaching a tool twice attaches it once; once the application has
    /// begun to shut down, attaching throws <see cref="InvalidOperationException"/>. A tool whose
    /// <see cref="IDebugTool.Attach"/> throws is detached and the exception goes on; a tool asked to detach, or caught
    /// by the shutdown, while it attaches is detached as soon as Attach returns.</summary>
    void AttachDebugTool(IDebugTool tool);

    /// <summary>Stops <paramref name="tool"/> on the calling thread, if it is attached; a tool still attaching is
    /// stopped when its Attach returns.</summary>
    void DetachDebugTool(IDebugTool tool);
}
