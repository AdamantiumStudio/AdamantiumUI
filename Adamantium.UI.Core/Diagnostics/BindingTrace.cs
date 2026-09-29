using System;
using System.Collections.Generic;
using System.Diagnostics;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Core.Diagnostics;

/// <summary>Binding failures - a path naming a member its object does not have, an <c>{Ancestor}</c> that found nothing.
/// Always on: each distinct message goes once to the application log and, under a debugger, to the IDE's output.
/// <see cref="Sink"/> receives every message as well. A binding that breaks is written down only if it is still broken
/// once the binding queue has been flushed AND one fresh attempt fails too: a template bound before its item arrived, or
/// a name further down a page still being built, is not a failure. A binding that stays broken is reported once.</summary>
public static class BindingTrace
{
    private static readonly HashSet<string> Reported = [];
    private static readonly List<(BindingExpressionBase Expression, string Message)> Suspects = [];

    [ThreadStatic]
    private static bool _retrying;

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

        Serilog.Log.Logger.Warning("Binding: {Message}", message);
        if (Debugger.IsLogging())
        {
            Debugger.Log(0, "Binding", $"Binding: {message}{Environment.NewLine}");
        }
    }

    internal static void Suspect(BindingExpressionBase expression, string message)
    {
        if (_retrying)
        {
            return;
        }

        lock (Suspects)
        {
            Suspects.Add((expression, message));
        }

        LoopSignal.Request();
    }

    internal static (BindingExpressionBase Expression, string Message)[] TakeSuspects()
    {
        lock (Suspects)
        {
            if (Suspects.Count == 0)
            {
                return [];
            }

            var taken = Suspects.ToArray();
            Suspects.Clear();
            return taken;
        }
    }

    internal static void Confirm((BindingExpressionBase Expression, string Message)[] suspects)
    {
        foreach (var (expression, message) in suspects)
        {
            if (expression.Status != BindingStatus.PathError || expression.Failure != message)
            {
                continue;
            }

            _retrying = true;
            try
            {
                expression.Retry();
            }
            finally
            {
                _retrying = false;
            }

            if (expression.Status == BindingStatus.PathError)
            {
                Log(expression.Failure);
            }
        }
    }
}
