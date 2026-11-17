using System;
using System.Collections.Generic;
using System.Reflection;
using Adamantium.Multiverse;

namespace Adamantium.UITests;

/// <summary>A universe that only records what is called on it; made with <see cref="Make"/>.</summary>
public class RecordingUniverse : DispatchProxy
{
    public List<string> Calls { get; } = [];

    public static IUniverse Make(out RecordingUniverse recorder)
    {
        var universe = Create<IUniverse, RecordingUniverse>();
        recorder = (RecordingUniverse)(object)universe;
        return universe;
    }

    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        Calls.Add(targetMethod.Name);
        var returned = targetMethod.ReturnType;
        return returned.IsValueType && returned != typeof(void) ? Activator.CreateInstance(returned) : null;
    }
}
