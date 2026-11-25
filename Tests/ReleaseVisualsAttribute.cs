using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Animation;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

[assembly: ReleaseVisuals]

/// <summary>Lets go of what a test built once it is over, and of the fixture's once its tests are. Their windows are never
/// closed, and an open window is kept by what outlives it - a brush shared by the process, the marks of a frame never drawn - with its whole theme; NUnit
/// keeps the fixture itself to the end of the run.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class ReleaseVisualsAttribute : Attribute, ITestAction
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                                BindingFlags.DeclaredOnly;

    private static readonly IEnumerable<KeyValuePair<IUIComponent, LayoutManager>> LayoutRoots =
        (IEnumerable<KeyValuePair<IUIComponent, LayoutManager>>)typeof(LayoutManager)
            .GetField("Managers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);

    // What the fixture built before its tests stays until it is over; weakly, so that nothing is kept by being listed.
    private readonly ConditionalWeakTable<IUIComponent, object> _builtBefore = new();

    public ActionTargets Targets => ActionTargets.Suite | ActionTargets.Test;

    public void BeforeTest(ITest test)
    {
        if (test.IsSuite)
        {
            return;
        }

        _builtBefore.Clear();
        foreach (var root in LayoutRoots)
        {
            _builtBefore.Add(root.Key, null);
        }
    }

    public void AfterTest(ITest test)
    {
        if (test.IsSuite)
        {
            if (test.Fixture is not { } fixture)
            {
                return;
            }

            ForgetFields(fixture);
            _builtBefore.Clear();
        }

        LoopSignal.Drain();   // what a test posted to a loop that never ran
        AnimationManager.Reset();
        // The tool tip is the process's one, shown in window after window.
        DiscardedVisuals.ReleaseNow(LayoutRoots.Select(root => root.Key)
            .Where(root => root is not ToolTip && !_builtBefore.TryGetValue(root, out _)).ToList());
        foreach (var scope in RenderDirtyRouter.All())
        {
            scope.Clear();
        }
    }

    private static void ForgetFields(object fixture)
    {
        for (var type = fixture.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in type.GetFields(InstanceFields))
            {
                if (!field.IsInitOnly && !field.FieldType.IsValueType)
                {
                    field.SetValue(fixture, null);
                }
            }
        }
    }
}
