using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;
using NUnit.Framework;

namespace Adamantium.UITests;

// Every routed event of the framework calls a handler of its own type through the call the generator wrote, never
// through DynamicInvoke - which also keeps it working where no code can be generated at run time (iOS, NativeAOT).
[TestFixture]
public class RoutedEventArgsInvokeTests
{
    private static MethodBase _reflected;
    private static bool _called;

    [Test]
    public void TheArgsOfEveryFrameworkEvent_CallItsHandlerType_Directly()
    {
        Assembly[] framework =
            [typeof(RoutedEvent).Assembly, typeof(InputUIComponent).Assembly, typeof(UI.UIApplication).Assembly];
        foreach (var type in framework.SelectMany(a => a.GetTypes()))
        {
            if (type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(f => f.FieldType == typeof(RoutedEvent)))
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
        }

        var probe = typeof(RoutedEventArgsInvokeTests).GetMethod(nameof(Probe), BindingFlags.NonPublic | BindingFlags.Static)!;
        List<string> indirect = [];
        var checkedEvents = 0;
        foreach (var routedEvent in EventManager.GetRoutedEvents())
        {
            if (!framework.Contains(routedEvent.EventOwnerType.Assembly))
            {
                continue;
            }

            checkedEvents++;
            var argsType = routedEvent.EventHandlerType.GetMethod("Invoke")!.GetParameters()[1].ParameterType;
            var handler = Delegate.CreateDelegate(routedEvent.EventHandlerType, probe.MakeGenericMethod(argsType));
            var args = (RoutedEventArgs)RuntimeHelpers.GetUninitializedObject(argsType);

            _reflected = null;
            _called = false;
            new EventSubscription { Handler = handler }.Invoke(null, args);

            if (!_called || _reflected != null)
            {
                indirect.Add($"{routedEvent} ({argsType.Name}): its {routedEvent.EventHandlerType.Name} was called " +
                             $"through {_reflected?.DeclaringType?.Name}.{_reflected?.Name}");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(checkedEvents, Is.GreaterThan(40), "the framework's events were all registered");
            Assert.That(indirect, Is.Empty);
        });
    }

    // Up to the test, no frame of reflection's invoking: the JIT may inline the direct calls, never that.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Probe<T>(object sender, T e)
    {
        _called = true;
        foreach (var frame in new StackTrace(1).GetFrames())
        {
            var method = frame.GetMethod();
            if (method == null || method.Name == nameof(TheArgsOfEveryFrameworkEvent_CallItsHandlerType_Directly))
            {
                return;
            }

            var owner = method.DeclaringType;
            if (owner == typeof(Delegate) || owner?.Namespace?.StartsWith("System.Reflection") == true ||
                (owner?.Name.Contains("InvokeStub") ?? false) || method.Name.Contains("InvokeStub"))
            {
                _reflected = method;
                return;
            }
        }
    }
}
