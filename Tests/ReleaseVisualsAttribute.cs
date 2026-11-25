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

/// <summary>Lets go of what a test built once it is over, and of the fixture's once the next fixture's tests begin. Their
/// windows are never closed, and an open window is kept by what outlives it - a brush shared by the process, the marks of a
/// frame never drawn - with its whole theme; NUnit keeps the fixture itself to the end of the run. A window of the
/// fixture's loses its running animations and its marks after each test.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class ReleaseVisualsAttribute : Attribute, ITestAction
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                                BindingFlags.DeclaredOnly;

    private static readonly IEnumerable<KeyValuePair<IUIComponent, LayoutManager>> LayoutRoots =
        (IEnumerable<KeyValuePair<IUIComponent, LayoutManager>>)typeof(LayoutManager)
            .GetField("Managers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);

    // The process's one tool tip, shown in window after window.
    private static readonly FieldInfo SharedToolTip =
        typeof(ToolTipService).GetField("_toolTip", BindingFlags.Static | BindingFlags.NonPublic)!;

    // What was there before the test, weakly, so that nothing is kept by being listed.
    private readonly ConditionalWeakTable<IUIComponent, object> _builtBefore = new();
    private object _fixture;

    public ActionTargets Targets => ActionTargets.Test;

    // NUnit tells an assembly's action of no fixture's end: the next one's first test stands for it.
    public void BeforeTest(ITest test)
    {
        if (!ReferenceEquals(test.Fixture, _fixture))
        {
            if (_fixture != null)
            {
                ForgetFields(_fixture);
                Release(root => _builtBefore.TryGetValue(root, out _));
            }

            _fixture = test.Fixture;
        }

        _builtBefore.Clear();
        foreach (var root in LayoutRoots)
        {
            _builtBefore.Add(root.Key, null);
        }
    }

    public void AfterTest(ITest test) => Release(root => !_builtBefore.TryGetValue(root, out _));

    private static void Release(Func<IUIComponent, bool> isDone)
    {
        LoopSignal.Drain();   // what a test posted to a loop that never ran
        AnimationManager.Reset();
        var toolTip = SharedToolTip.GetValue(null);
        DiscardedVisuals.ReleaseNow(LayoutRoots.Select(root => root.Key)
            .Where(root => !ReferenceEquals(root, toolTip) && isDone(root)).ToList());
        LoopSignal.Drain();   // what letting go posted
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
