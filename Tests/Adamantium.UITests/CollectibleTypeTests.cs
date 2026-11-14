using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Core.TypeParsers;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What the UI keeps of a type that can be unloaded - a game's code, a plugin: nothing past its use. Each case
/// held such a type for the life of the application, and with it every type of its code.</summary>
[TestFixture]
public class CollectibleTypeTests
{
    // A template set remembers the template it picked by the item's type.
    [Test]
    public void ATemplateSet_DoesNotKeepATypeThatCanBeUnloaded()
    {
        var templates = new DataTemplateSet();
        templates.Templates.Add(new DataTemplate(() => new TemplateResult { RootComponent = new Border() }));

        var type = PickFor(templates);

        Assert.That(Collected(type), Is.True);
    }

    // The index style selectors name types by takes every assembly loaded.
    [Test]
    public void TheSelectorIndex_DoesNotKeepAnAssemblyThatCanBeUnloaded()
    {
        var assembly = IndexedAssembly();

        Assert.That(Collected(assembly), Is.True);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PickFor(DataTemplateSet templates)
    {
        var type = CollectibleType();
        var picked = templates.SelectTemplate(Activator.CreateInstance(type), null);
        Assert.That(picked, Is.Not.Null);
        return new WeakReference(type);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference IndexedAssembly()
    {
        var type = CollectibleType();
        SelectorParser.IndexLoadedAssemblies();
        return new WeakReference(type.Assembly);
    }

    private static Type CollectibleType()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Collectible" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Collectible");
        var type = module.DefineType("Collectible.Thing", TypeAttributes.Public);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        return type.CreateType();
    }

    private static bool Collected(WeakReference reference)
    {
        for (var attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return !reference.IsAlive;
    }
}
