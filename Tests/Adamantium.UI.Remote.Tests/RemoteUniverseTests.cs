using System.Collections.Concurrent;
using Adamantium.Graphics.Core;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Remote;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
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
        _universe = new RemoteUniverse(_panel, Gpu);
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

    [Test]
    public void Disposed_ItLetsGoOfTheConnection()
    {
        _universe.Dispose();

        Assert.That(_served.Completion.Wait(Patience), Is.True);
    }

    // Never imported here: nothing draws the panel.
    private static RemoteSurface SurfaceInMemory() => new()
    {
        Width = 4,
        Height = 4,
        Format = Format.B8G8R8A8_UNORM,
        HandleType = SharedHandleType.HostMemory,
        Buffers = [new RemoteSurfaceBuffer(0, 64, 0, 0, 4096, 64)],
        MemoryPath = "frames"
    };

    private uint Pixels(double points) => (uint)Math.Round(points * _window.DpiScale.X);

    private static T Take<T>(BlockingCollection<T> collection, string what)
    {
        Assert.That(collection.TryTake(out var item, Patience), Is.True, what);
        return item;
    }
}
