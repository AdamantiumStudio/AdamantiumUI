using System;
using System.Collections.Generic;
using System.Reflection;
using Adamantium.Multiverse;

namespace Adamantium.UITests;

/// <summary>A universe that only records what is called on it, stopped once shut down unless <see cref="IsStuck"/>;
/// made with <see cref="Make"/>.</summary>
public class RecordingUniverse : DispatchProxy
{
    private readonly object _gate = new();
    private readonly List<string> _calls = [];

    /// <summary>The members called on it, in order.</summary>
    public List<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Whether its loop is stuck in a frame: shut down, it does not stop.</summary>
    public bool IsStuck { get; set; }

    public static IUniverse Make(out RecordingUniverse recorder)
    {
        var universe = Create<IUniverse, RecordingUniverse>();
        recorder = (RecordingUniverse)(object)universe;
        return universe;
    }

    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        lock (_gate)
        {
            _calls.Add(targetMethod.Name);
            if (targetMethod.Name == "get_IsStopped")
            {
                return !IsStuck && _calls.Contains("ShutDown");
            }
        }

        var returned = targetMethod.ReturnType;
        return returned.IsValueType && returned != typeof(void) ? Activator.CreateInstance(returned) : null;
    }
}
