using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Adamantium.UI.Core.Diagnostics;

/// <summary>Values that could not be set: an element asked to set a property by a name it has no property for. Always
/// on: each distinct message goes once to the application log and, under a debugger, to the IDE's output.
/// <see cref="Sink"/> receives every message as well. The value is dropped and the application goes on.</summary>
public static class PropertyTrace
{
    private static readonly HashSet<string> Reported = [];

    public static Action<string> Sink;

    public static void Log(string message)
    {
        Sink?.Invoke(message);

        lock (Reported)
        {
            if (!Reported.Add(message))
            {
                return;
            }
        }

        Serilog.Log.Logger.Warning("Property: {Message}", message);
        if (Debugger.IsLogging())
        {
            Debugger.Log(0, "Property", $"Property: {message}{Environment.NewLine}");
        }
    }
}
