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

    /// <summary>Whether the application keeps what it remembers between runs - window places, layouts - in a fresh folder
    /// of its own (<see cref="ApplicationData.Folder"/>), deleted once the session has closed it: the run starts as the
    /// first one ever did and leaves nothing for the next. An application left running keeps its folder.</summary>
    public bool FreshData { get; set; }

    /// <summary>Whether the application checks every frame it draws against its tree (the frame verifier) from its start;
    /// true by default. A frame that parts from the tree is an error in the <see cref="ErrorJournal"/>, so the step after
    /// which it was drawn fails - what a broken render cache shows is caught by a run, not by someone's eye.</summary>
    public bool VerifyFrames { get; set; } = true;
}
