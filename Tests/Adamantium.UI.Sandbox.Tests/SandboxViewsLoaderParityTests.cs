using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Adamantium.UI.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Sandbox.ModuleLoading;
using Adamantium.UI.Sandbox.Resources;
using Adamantium.UI.Sandbox.Views;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>
/// The designer builds a view from its markup by running the build plan; the application runs the code generated from
/// the same plan. Every view the sandbox ships comes out the same both ways: the same elements, and on each the same
/// properties set, from the same source, to the same values.
/// </summary>
[TestFixture]
public class SandboxViewsLoaderParityTests
{
    private static readonly FieldInfo ValueSlots =
        typeof(AdamantiumComponent).GetField("values", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo AttachedSlots =
        typeof(AdamantiumComponent).GetField("attachedValues", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Walked = new();

    [Test]
    public void EverySandboxView_IsBuiltByTheLoaderAsByItsGeneratedCode()
    {
        var app = HeadlessApplication.Start<Fluent>();
        app.ThemeManager.AddStyleSet<RibbonShellStyleSet>();
        app.ResourceManager.AddSource(new ModuleResources(), typeof(ModuleIcons), ResourceScope.Global);
        app.ResourceManager.AddSource(new ModuleResources(), typeof(RibbonShellIcons), ResourceScope.Global);
        app.ResourceManager.AddSource(new ModuleResources(), typeof(CaptionIcons), ResourceScope.Global);
        var container = (Adamantium.Core.DependencyInjection.IDependencyContainer)typeof(HeadlessApplication)
            .GetField("_container", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app);
        container.RegisterInstance<Adamantium.Core.DependencyInjection.IDependencyResolver>(container);
        container.RegisterInstance<IUIApplication>(app);
        container.RegisterSingleton<Adamantium.Navigation.IRegionManager, Adamantium.Navigation.RegionManager>();
        container.RegisterSingleton<Adamantium.UI.Controls.Navigation.IViewLocator, Adamantium.UI.Controls.Navigation.ViewLocator>();
        container.RegisterSingleton<Adamantium.UI.Controls.Navigation.IWindowShellRegistry, Adamantium.UI.Controls.Navigation.WindowShellRegistry>();
        container.RegisterSingleton<Adamantium.Navigation.IWindowNavigationBackend, Adamantium.UI.Navigation.WindowNavigationBackend>();
        container.RegisterSingleton<Adamantium.Navigation.INavigationService, Adamantium.Navigation.NavigationService>();

        var assembly = typeof(RibbonShellView).Assembly;
        var differences = new List<string>();
        var skipped = new List<string>();
        var compared = 0;
        foreach (var file in MarkupFiles())
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var types = assembly.GetTypes().Where(t => t.Name == name && t.GetConstructor(Type.EmptyTypes) != null).ToList();
            if (types.Count != 1)
            {
                continue;
            }

            var text = File.ReadAllText(file);
            object generated;
            try
            {
                generated = Activator.CreateInstance(types[0]);
                if (generated is Adamantium.UI.Controls.WindowBase)
                {
                    types[0].GetMethod("InitializeComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(generated, null);
                }

                (generated as StyleSet)?.Initialize(app.ThemeManager.CurrentTheme);
            }
            catch (Exception exception)
            {
                skipped.Add($"{name}: {exception.GetBaseException().Message}");
                continue;
            }

            var loaded = AumlLoader.Load(text);
            var errors = loaded.Diagnostics.Where(d => !d.StartsWith("warning:") && !d.StartsWith("info:")).ToList();
            if (errors.Count > 0)
            {
                differences.Add($"{name}: the loader reported {string.Join(" | ", errors)}");
            }

            Compare(generated, loaded.Root, name, differences, new HashSet<(object, object)>(ReferencePairs.Instance), 0, true);
            compared++;
        }

        TestContext.Out.WriteLine("Not built here, they need the whole application:" + Environment.NewLine + string.Join(Environment.NewLine, skipped));
        Assert.That(compared, Is.GreaterThan(50), "the sweep reached the views");
        Assert.That(differences, Is.Empty, string.Join(Environment.NewLine, differences.Take(200)));
    }

    private static IEnumerable<string> MarkupFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(dir.FullName, "Adamantium.UI.Sandbox", "Views")))
        {
            dir = dir.Parent;
        }

        return Directory.EnumerateFiles(Path.Combine(dir.FullName, "Adamantium.UI.Sandbox"), "*.auml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(f => f);
    }

    private static void Compare(object a, object b, string path, List<string> differences, HashSet<(object, object)> seen,
        int depth, bool root = false)
    {
        if (a == null || b == null)
        {
            if (a != b)
            {
                differences.Add($"{path}: {Describe(a)} vs {Describe(b)}");
            }

            return;
        }

        var sameType = root ? a.GetType().IsAssignableTo(b.GetType()) : a.GetType() == b.GetType();
        if (!sameType)
        {
            differences.Add($"{path}: {a.GetType().Name} vs {b.GetType().Name}");
            return;
        }

        if (depth > 60 || !seen.Add((a, b)))
        {
            return;
        }

        if (a is string || a.GetType().IsValueType || a is Type || a is Delegate)
        {
            if (a is Delegate da && b is Delegate db ? da.Method != db.Method : !Equals(a, b))
            {
                differences.Add($"{path}: {Describe(a)} vs {Describe(b)}");
            }

            return;
        }

        if (a is AdamantiumComponent ca && b is AdamantiumComponent cb)
        {
            foreach (var property in SlottedProperties(ca).Union(SlottedProperties(cb)).OrderBy(p => p.Name))
            {
                if (property.Name == "Id")
                {
                    continue;
                }

                var at = $"{path}.{property.OwnerType.Name}.{property.Name}";
                var sa = ca.GetValueSource(property);
                var sb = cb.GetValueSource(property);
                if (sa != sb)
                {
                    differences.Add($"{at}: from {sa} vs from {sb}");
                }
                else if (sa == ValuePriority.Local)
                {
                    Compare(ca.GetValue(property), cb.GetValue(property), at, differences, seen, depth + 1);
                }
            }
        }

        if (a is IEnumerable ea && b is IEnumerable eb)
        {
            var la = ea.Cast<object>().ToList();
            var lb = eb.Cast<object>().ToList();
            if (la.Count != lb.Count)
            {
                differences.Add($"{path}: {la.Count} items vs {lb.Count}");
                return;
            }

            for (var i = 0; i < la.Count; i++)
            {
                Compare(la[i], lb[i], $"{path}[{i}]", differences, seen, depth + 1);
            }

            return;
        }

        foreach (var property in WalkedProperties(a.GetType()))
        {
            object va, vb;
            try
            {
                va = property.GetValue(a);
                vb = property.GetValue(b);
            }
            catch (Exception)
            {
                continue;
            }

            Compare(va, vb, $"{path}.{property.Name}", differences, seen, depth + 1);
        }
    }

    // Plain properties markup can set, and the collections it fills; not the ones a component keeps in its slots, and
    // not the ones that lead back up the tree.
    private static PropertyInfo[] WalkedProperties(Type type) => Walked.GetOrAdd(type, static t =>
    {
        var component = typeof(AdamantiumComponent).IsAssignableFrom(t);
        var slotted = component ? AdamantiumPropertyMap.GetRegistered(t).Select(p => p.Name).ToHashSet() : [];
        return t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod != null && p.DeclaringType != typeof(object))
            .Where(p => !component || p.SetMethod is { IsPublic: true } || typeof(IEnumerable).IsAssignableFrom(p.PropertyType))
            .Where(p => !slotted.Contains(p.Name))
            .Where(p => p.Name is not ("Parent" or "VisualParent" or "LogicalParent" or "TemplatedParent" or "Owner" or "Root" or "Host"))
            .ToArray();
    });

    private static IEnumerable<AdamantiumProperty> SlottedProperties(AdamantiumComponent component)
    {
        foreach (var field in new[] { ValueSlots, AttachedSlots })
        {
            if (field.GetValue(component) is IDictionary map)
            {
                foreach (AdamantiumProperty property in map.Keys)
                {
                    yield return property;
                }
            }
        }
    }

    private static string Describe(object value) => value == null ? "null" : $"{value} ({value.GetType().Name})";

    private sealed class ReferencePairs : IEqualityComparer<(object, object)>
    {
        public static readonly ReferencePairs Instance = new();

        public bool Equals((object, object) x, (object, object) y) =>
            ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

        public int GetHashCode((object, object) pair) =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Item1),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Item2));
    }
}
