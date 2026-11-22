using System;
using System.Collections.Generic;

namespace Adamantium.UI;

/// <summary>A copy of the application started past <see cref="UIApplication.MaxInstances"/> handed the running one
/// what it was started with.</summary>
public sealed class AnotherCopyStartedEventArgs(IReadOnlyList<string> arguments, string workingDirectory) : EventArgs
{
    /// <summary>Its command line, without the program.</summary>
    public IReadOnlyList<string> Arguments { get; } = arguments;

    /// <summary>The folder it was started in: a relative path among <see cref="Arguments"/> is relative to it, not to
    /// this copy's.</summary>
    public string WorkingDirectory { get; } = workingDirectory;

    /// <summary>Set when the application answered it; left unset, the main window comes to the front.</summary>
    public bool Handled { get; set; }
}
