using System;
using System.Runtime.CompilerServices;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>An element's own resources leave with the element: the resource manager outlives every view.</summary>
[TestFixture]
public class LocalResourcesReleaseTests
{
    private ResourceManager _rm;
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer());
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void FreshResources()
    {
        _rm = new ResourceManager();
        _app.ResourceManager = _rm;
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
    }

    private static Border WithLocalResources()
    {
        var owner = new Border();
        var resources = new ResourceDictionary();
        resources.Add("Word", "local");
        ResourceContext.SetResources(owner, resources);
        return owner;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Discarded()
    {
        var owner = WithLocalResources();
        DiscardedVisuals.Publish(owner);
        return new WeakReference(owner);
    }

    [Test]
    public void ADiscardedOwnerTakesItsLocalResourcesWithIt()
    {
        var owner = WithLocalResources();
        Assert.That(_rm.FindResource(owner, "Word"), Is.EqualTo("local"), "the element's own resource resolves");

        DiscardedVisuals.Publish(owner);
        DiscardedVisuals.Drain(int.MaxValue);

        Assert.That(_rm.FindResource(owner, "Word"), Is.Null, "the dictionary outlived the element that declared it");
    }

    [Test]
    public void ADiscardedOwnerIsNotKeptByTheResourceManager()
    {
        var gone = Discarded();
        DiscardedVisuals.Drain(int.MaxValue);
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.That(gone.IsAlive, Is.False, "the resource manager held a destroyed element through its dictionary");
    }
}
