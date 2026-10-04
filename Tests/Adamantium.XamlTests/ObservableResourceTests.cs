using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// {ObservableResource} is the LIVE, tree-scoped keyed-resource marker: unlike {ResourceReference} (resolved once), it
/// re-resolves when the resource set changes (a theme swap, or a dictionary loaded/unloaded), which the ResourceManager
/// signals once per layout pass via <see cref="ResourceManager.FlushResourceChanges"/>. This drives that real path.
/// </summary>
[TestFixture]
public class ObservableResourceTests
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
        // A fresh ResourceManager per test: the context is process-global, so without this the Global provider would
        // accumulate each test's dictionaries and leak values across tests.
        _rm = new ResourceManager();
        _app.ResourceManager = _rm;
        // UIAppContext.Initialize is idempotent (Current ??= app), so if another XamlTests fixture initialized first, our
        // Initialize was a no-op and Current points at ITS app (ResourceManager == null). Force ours for our tests - the
        // other fixtures use only a no-op ThemeContext + resolver, which ours provides too, so this doesn't disturb them.
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
    }

    [Test]
    public void ObservableResource_ReResolves_WhenResourceSetChanges()
    {
        var owner = new Border();
        _rm.AddSource(owner, typeof(ResourcesV1), ResourceScope.Global);

        // Connect the live marker to a property; it resolves immediately.
        var button = new Button();
        new ObservableResource("AccentColor").Apply(button, "Content");
        Assert.That(button.Content, Is.EqualTo("RED"), "initial resolve");

        // Swap the dictionary (as a theme swap would) and flush - the live marker must pick up the new value.
        _rm.RemoveSources(owner);
        _rm.AddSource(owner, typeof(ResourcesV2), ResourceScope.Global);
        _rm.FlushResourceChanges();

        Assert.That(button.Content, Is.EqualTo("BLUE"), "live: re-resolved after the resource set changed");
    }

    [Test]
    public void ObservableResource_TransientMiss_DoesNotClobberLastValue()
    {
        var owner = new Border();
        _rm.AddSource(owner, typeof(ResourcesV1), ResourceScope.Global);

        var button = new Button();
        new ObservableResource("AccentColor").Apply(button, "Content");
        Assert.That(button.Content, Is.EqualTo("RED"));

        // Remove the source (nothing resolves the key now) and flush: a transient miss must NOT null out the property.
        _rm.RemoveSources(owner);
        _rm.FlushResourceChanges();
        Assert.That(button.Content, Is.EqualTo("RED"), "a resolve miss keeps the last good value, never clobbers to null");
    }

    // A visual root, so the tree attach that brings an unloaded element back actually happens.
    private sealed class Root : Adamantium.UI.Controls.Panels.Grid, IRootVisualComponent
    {
        public Adamantium.Mathematics.Vector2 PointToClient(PixelPoint point) => new((float)point.X, (float)point.Y);
        public PixelPoint PointToScreen(Adamantium.Mathematics.Vector2 point) => new(point.X, point.Y);
        public PixelPoint Position { get; set; }
        public void AttachContextAndInitialize(IUIContext context) { }
        public double Left { get; set; }
        public double Top { get; set; }
        public string Title { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public IUIContext UIContext => null;
    }

    // What a template teardown raises on every element under it - including content only passing through, on its way
    // to another tree.
    private static void Unload(Button button) =>
        button.RaiseEvent(new Adamantium.UI.Core.RoutedEvents.RoutedEventArgs(
            Adamantium.UI.Controls.Base.InputUIComponent.UnloadedEvent, button));

    [Test]
    public void ObservableResource_UnloadedAndPutBackInATree_FollowsTheResourceAgain()
    {
        var owner = new Border();
        _rm.AddSource(owner, typeof(ResourcesV1), ResourceScope.Global);

        var root = new Root();
        var button = new Button();
        root.Children.Add(button);
        new ObservableResource("AccentColor").Apply(button, "Content");

        // Moved: unloaded by the tree it leaves, out of any tree for a while.
        Unload(button);
        root.Children.Remove(button);
        _rm.RemoveSources(owner);
        _rm.AddSource(owner, typeof(ResourcesV2), ResourceScope.Global);
        _rm.FlushResourceChanges();
        Assert.That(button.Content, Is.EqualTo("RED"), "out of every tree it listens to nothing");

        root.Children.Add(button);
        Assert.That(button.Content, Is.EqualTo("BLUE"), "back in a tree, it reads the resource again");

        _rm.RemoveSources(owner);
        _rm.AddSource(owner, typeof(ResourcesV1), ResourceScope.Global);
        _rm.FlushResourceChanges();
        Assert.That(button.Content, Is.EqualTo("RED"), "...and follows it again");
    }

    // Waiting for it to come back holds nothing but the element itself: an element thrown away for good is collected.
    // Tag rather than Content - a layout property would put the element in a layout queue, which is not what is asked.
    [Test]
    public void ObservableResource_UnloadedAndThrownAway_IsNotKeptAlive()
    {
        var owner = new Border();
        _rm.AddSource(owner, typeof(ResourcesV1), ResourceScope.Global);

        var gone = ThrownAway();
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.That(gone.IsAlive, Is.False);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ThrownAway()
    {
        var button = new Button();
        new ObservableResource("AccentColor").Apply(button, "Tag");
        Unload(button);
        return new WeakReference(button);
    }

    // The resource manager outlives every element: unloaded, the element is no longer among its listeners.
    [Test]
    public void ObservableResource_Unloaded_LeavesTheResourceManager()
    {
        var owner = new Border();
        _rm.AddSource(owner, typeof(ResourcesV1), ResourceScope.Global);
        var changed = typeof(ResourceManager).GetField(nameof(ResourceManager.ResourcesChanged),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        int Listeners() => (changed.GetValue(_rm) as Delegate)?.GetInvocationList().Length ?? 0;

        var before = Listeners();
        var button = new Button();
        new ObservableResource("AccentColor").Apply(button, "Tag");
        Assert.That(Listeners(), Is.EqualTo(before + 1));

        Unload(button);

        Assert.That(Listeners(), Is.EqualTo(before));
    }

    private sealed class ResourcesV1 : ResourceDictionary
    {
        protected override void OnInitialize() => Add("AccentColor", "RED");
    }

    private sealed class ResourcesV2 : ResourceDictionary
    {
        protected override void OnInitialize() => Add("AccentColor", "BLUE");
    }
}
