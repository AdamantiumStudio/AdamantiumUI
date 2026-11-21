using System.Collections.Concurrent;
using Adamantium.Graphics.Core;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Remote;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Extensions;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UI.Remote.Tests;

/// <summary>A panel showing a universe of another process, over the pipe a host serves: the launch made at the panel's
/// size, the panel's keys, buttons, pointer and size given to the host, the surfaces it shows bound to the panel.</summary>
[TestFixture]
public class RemoteUniverseTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly Guid Gpu = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

    private Window _window;
    private RenderTargetPanel _panel;
    private RecordingHost _host;
    private UniverseViewerConnection _served;
    private RemoteUniverse _universe;

    [SetUp]
    public void Connect()
    {
        _panel = new RenderTargetPanel { Background = Brushes.Gray };
        _window = new Window { Width = 400, Height = 300, Content = _panel };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        _host = new RecordingHost();
        var pipeName = "adamantium-remote-universe-" + Guid.NewGuid().ToString("N");
        using var connecting = new CancellationTokenSource(Patience);
        var accepting = UniverseViewerConnection.AcceptAsync(pipeName, _host, connecting.Token);
        _universe = new RemoteUniverse(Gpu);
        _universe.ShowIn(_panel);
        _universe.Attach(UniverseHostConnection.ConnectAsync(pipeName, _universe.Viewer, connecting.Token)
            .GetAwaiter().GetResult());
        _served = accepting.GetAwaiter().GetResult();
    }

    [TearDown]
    public void Disconnect()
    {
        _universe.Dispose();
        _served.Dispose();
    }

    [Test]
    public async Task TheLaunch_IsMadeAtThePanelsSizeInPixels_OnTheViewersGpu()
    {
        await _universe.LaunchAsync(new UniverseLaunch { ScenePath = "Scene.ascene" }, CancellationToken.None);

        var launch = Take(_host.Launches, "launched");
        Assert.Multiple(() =>
        {
            Assert.That(launch.ScenePath, Is.EqualTo("Scene.ascene"));
            Assert.That(launch.Width, Is.EqualTo(Pixels(_panel.ActualWidth)));
            Assert.That(launch.Height, Is.EqualTo(Pixels(_panel.ActualHeight)));
            Assert.That(launch.ViewerDeviceUuid, Is.EqualTo(Gpu));
        });
    }

    [Test]
    public void AKeyPressedOnThePanel_ReachesTheHost()
    {
        _panel.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.W, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        });

        var key = Take(_host.Keys, "the key");
        Assert.Multiple(() =>
        {
            Assert.That(key.Key, Is.EqualTo(Keys.W));
            Assert.That(key.InputType, Is.EqualTo(InputType.Down));
        });
    }

    [Test]
    public void APressWithAltHeld_HoldsAltOnTheHost_BeforeTheButton()
    {
        _panel.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Right,
            InputModifiers.LeftAlt | InputModifiers.RightMouseButton, default)
        {
            RoutedEvent = PointerEvents.PointerPressedEvent
        });

        var alt = Take(_host.Keys, "Alt");
        var button = Take(_host.Mouse, "the button");
        Mouse.Pointer.Capture(null);
        Assert.Multiple(() =>
        {
            Assert.That(alt.Key, Is.EqualTo(Keys.LeftAlt));
            Assert.That(alt.InputType, Is.EqualTo(InputType.Down));
            Assert.That(button.Button, Is.EqualTo(Adamantium.Multiverse.Input.MouseButton.Right));
            Assert.That(button.InputType, Is.EqualTo(InputType.Down));
        });
    }

    [Test]
    public async Task ThePanelResized_ResizesTheHost()
    {
        await _universe.LaunchAsync(new UniverseLaunch(), CancellationToken.None);
        Take(_host.Sizes, "the launch's size");

        _panel.Width = 250;
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        Assert.That(Take(_host.Sizes, "the new size"), Does.StartWith($"{Pixels(250)}x"));
    }

    [Test]
    public void ASurfaceTheHostShows_IsBoundToThePanel_AndLetGoOnceTheUniverseEnds()
    {
        _served.Viewer.ShowSurface(SurfaceInMemory());
        var bound = SpinWait.SpinUntil(() => _panel.Source != null, Patience);
        var source = _panel.Source;

        _served.Viewer.EndUniverse();
        var ended = _universe.Ended.Wait(Patience);

        Assert.Multiple(() =>
        {
            Assert.That(bound, Is.True, "bound");
            Assert.That(source?.MemoryPath, Is.EqualTo("frames"));
            Assert.That(ended, Is.True, "told the universe ended");
            Assert.That(_panel.Source, Is.Null, "let go of once it ended");
        });
    }

    [Test]
    public void EachFrameThePanelTakes_AsksTheHostForTheNext()
    {
        _served.Viewer.ShowSurface(SurfaceInMemory());
        Assert.That(SpinWait.SpinUntil(() => _panel.Source != null, Patience), Is.True, "bound");
        _served.Viewer.ShowFrame(7);
        Assert.That(SpinWait.SpinUntil(() => _panel.Source.Submitted == 7, Patience), Is.True, "told of frame 7");

        _panel.Source.Demand.Raise();
        var first = Take(_host.AskedFrames, "asked after the first frame taken");
        _panel.Source.Demand.Raise();
        var second = Take(_host.AskedFrames, "asked after the second");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(8ul));
            Assert.That(second, Is.EqualTo(8ul));
        });
    }

    // Alt held in the view, then the focus taken elsewhere, where Alt is let go: the next press with Alt holds it again.
    [Test]
    public void AKeyHeldWhenThePanelLosesTheFocus_IsForgotten()
    {
        _panel.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.LeftAlt, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        });
        Take(_host.Keys, "Alt down");
        _panel.RaiseEvent(new KeyboardFocusChangedEventArgs(_panel, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });

        _panel.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Right,
            InputModifiers.LeftAlt | InputModifiers.RightMouseButton, default)
        {
            RoutedEvent = PointerEvents.PointerPressedEvent
        });
        Mouse.Pointer.Capture(null);

        var alt = Take(_host.Keys, "Alt held again for the press");
        Assert.That(alt.Key, Is.EqualTo(Keys.LeftAlt));
    }

    // Clicked into, in the window worked in: the host hears it has the keyboard. Told as the panel HOLDS it - GotFocus
    // comes before that, and the host told then heard it had none, the game's keys going nowhere.
    [Test]
    public void ThePanelFocused_TellsTheHostItHasTheKeyboard()
    {
        _window.SetValue(WindowBase.IsActiveProperty, true);

        _panel.Focus();

        var told = false;
        while (!told && _host.Focuses.TryTake(out var hasKeyboard, Patience))
            told = hasKeyboard;

        Assert.That(told, Is.True, "the host has the keyboard");
    }

    // The host's process went away: the panel lets go of its surface, and input to the panel goes nowhere.
    [Test]
    public void TheHostGone_ThePanelLetsGoOfItsSurface_AndInputGoesNowhere()
    {
        _served.Viewer.ShowSurface(SurfaceInMemory());
        Assert.That(SpinWait.SpinUntil(() => _panel.Source != null, Patience), Is.True, "bound");

        _served.Dispose();
        var ended = _universe.Ended.Wait(Patience);
        _panel.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.W, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        });

        Assert.Multiple(() =>
        {
            Assert.That(ended, Is.True, "ended");
            Assert.That(_panel.Source, Is.Null, "let go of");
        });
    }

    // A surface the host showed before the universe was shown anywhere is bound once it is; shown nowhere again, the
    // panel lets go of it and its keys no longer reach the host.
    [Test]
    public void ShownNowhere_ThenInAPanel_ThenNowhere_ThePanelTakesTheSurfaceAndGivesItBack()
    {
        _universe.ShowIn(null);
        _served.Viewer.ShowSurface(SurfaceInMemory());
        Assert.That(SpinWait.SpinUntil(() => _universe.HasSurface, Patience), Is.True, "the surface came");
        var unshown = _panel.Source;

        _universe.ShowIn(_panel);
        var shown = _panel.Source;
        _universe.ShowIn(null);
        _panel.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.W, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        });

        Assert.Multiple(() =>
        {
            Assert.That(unshown, Is.Null, "nothing bound while shown nowhere");
            Assert.That(shown?.MemoryPath, Is.EqualTo("frames"), "bound once shown");
            Assert.That(_panel.Source, Is.Null, "let go of once shown nowhere");
            Assert.That(_universe.Panel, Is.Null);
            Assert.That(_host.Keys.TryTake(out _, TimeSpan.FromMilliseconds(300)), Is.False, "a key of a panel left");
        });
    }

    // A surface bound to one panel is not imported again by another: the host is asked for a new one, and that is bound.
    [Test]
    public void MovedToAnotherPanel_ItAsksForANewSurface_AndBindsThatOne()
    {
        _served.Viewer.ShowSurface(SurfaceInMemory("first"));
        Assert.That(SpinWait.SpinUntil(() => _panel.Source != null, Patience), Is.True, "bound to the first panel");
        var other = new RenderTargetPanel();

        _universe.ShowIn(other);
        var asked = SpinWait.SpinUntil(() => _host.NewSurfacesAsked == 1, Patience);
        var before = other.Source;
        _served.Viewer.ShowSurface(SurfaceInMemory("second"));
        var bound = SpinWait.SpinUntil(() => other.Source != null, Patience);

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.True, "a new surface asked for");
            Assert.That(before, Is.Null, "the first panel's surface not bound again");
            Assert.That(bound, Is.True, "the new one bound");
            Assert.That(other.Source?.MemoryPath, Is.EqualTo("second"));
            Assert.That(_panel.Source, Is.Null, "the first panel let go of its own");
        });
    }

    // The right button held as the universe leaves the panel: its release never reaches a listener gone, so it is sent.
    [Test]
    public void AButtonHeldAsThePanelIsLeft_IsLetGoOnTheHost()
    {
        _panel.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Right, InputModifiers.RightMouseButton,
            default)
        {
            RoutedEvent = PointerEvents.PointerPressedEvent
        });
        Take(_host.Mouse, "pressed");

        _universe.ShowIn(null);
        Mouse.Pointer.Capture(null);

        var release = Take(_host.Mouse, "let go");
        Assert.Multiple(() =>
        {
            Assert.That(release.Button, Is.EqualTo(Adamantium.Multiverse.Input.MouseButton.Right));
            Assert.That(release.InputType, Is.EqualTo(InputType.Up));
        });
    }

    // Through the behavior in markup: bound, the universe is shown in its panel; unbound, nowhere.
    [Test]
    public void TheHostBehavior_ShowsTheUniverseItIsGiven_AndNoneOnceItIsTakenAway()
    {
        _universe.ShowIn(null);
        var other = new RenderTargetPanel();
        var behavior = new RemoteUniverseHost();
        other.Behaviors.Add(behavior);

        behavior.Universe = _universe;
        var shownIn = _universe.Panel;
        behavior.Universe = null;

        Assert.Multiple(() =>
        {
            Assert.That(shownIn, Is.SameAs(other));
            Assert.That(_universe.Panel, Is.Null);
        });
    }

    // Moved to another panel by its owner, a universe stays there when the behavior of the first lets go.
    [Test]
    public void TheHostBehavior_LeavesAUniverseMovedToAnotherPanel()
    {
        var other = new RenderTargetPanel();
        var behavior = new RemoteUniverseHost();
        other.Behaviors.Add(behavior);
        behavior.Universe = _universe;

        _universe.ShowIn(_panel);
        behavior.Universe = null;

        Assert.That(_universe.Panel, Is.SameAs(_panel));
    }

    [Test]
    public void Disposed_ItLetsGoOfTheConnection()
    {
        _universe.Dispose();

        Assert.That(_served.Completion.Wait(Patience), Is.True);
    }

    // Never imported here: nothing draws the panel.
    private static RemoteSurface SurfaceInMemory(string path = "frames") => new()
    {
        Width = 4,
        Height = 4,
        Format = Format.B8G8R8A8_UNORM,
        HandleType = SharedHandleType.HostMemory,
        Buffers = [new RemoteSurfaceBuffer(0, 64, 0, 0, 4096, 64)],
        MemoryPath = path
    };

    private uint Pixels(double points) => (uint)Math.Round(points * _window.DpiScale.X);

    private static T Take<T>(BlockingCollection<T> collection, string what)
    {
        Assert.That(collection.TryTake(out var item, Patience), Is.True, what);
        return item;
    }
}
