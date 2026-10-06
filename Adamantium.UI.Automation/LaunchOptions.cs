using System;
using System.Collections.Generic;

namespace Adamantium.UI.Automation;

/// <summary>How <see cref="AutomationSession.LaunchAsync"/> starts an application.</summary>
public sealed class LaunchOptions
{
    /// <summary>Its command line.</summary>
    public string Arguments { get; set; }

    /// <summary>Where it starts; by default the folder it is in.</summary>
    public string WorkingDirectory { get; set; }

    /// <summary>Variables set for it alone, on top of this process's own.</summary>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>();

    /// <summary>The pipe its agent listens on; by default one of its own, so several can run at once.</summary>
    public string PipeName { get; set; }

    /// <summary>How long it may take to show its first window; a minute by default.</summary>
    public TimeSpan? StartTimeout { get; set; }

    /// <summary>Whether disposing the session closes the application; true by default. An application left running is
    /// started apart from this process, holding none of its handles, so a script that reads this process's output does
    /// not wait for the application to exit.</summary>
    public bool CloseOnDispose { get; set; } = true;
}
