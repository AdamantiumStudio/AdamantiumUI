using System.Diagnostics;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Multiverse.Remote;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;
using FrameSharing = Adamantium.Multiverse.Remote.FrameSharing;

namespace Adamantium.UI.Remote.Tests;

/// <summary>The engine's universe host started as the editor starts it, its frames shown in a panel: the lit cube in the
/// middle of the frames the panel takes, and newer frames coming as it takes them.</summary>
[TestFixture]
[Category("Gpu")]
public class RemoteUniverseHostTests
{
    private const string Scene = """
        <?xml version="1.0" encoding="utf-8"?>
        <Scene Version="1" Uid="70000000000000000000000000000000">
          <Lights>
            <Light Uid="70000000000000000000000000000001" Name="Sun" Position="0, -8, -6" Rotation="-0.9659259, 0, 0, 0.25881895" Type="Directional" Color="1, 1, 1" Intensity="1.5" Range="1" InnerSpotAngle="0" OuterSpotAngle="45" Shadows="false" ShadowMapResolution="0" />
          </Lights>
          <Cameras>
            <Camera Uid="70000000000000000000000000000002" Name="Eye" Position="0, 0, -46" />
          </Cameras>
          <Shapes>
            <Shape Uid="70000000000000000000000000000003" Name="Cube" Position="0, 0, -40" Type="Adamantium.ECS.Components.CubeShape" Color="1, 1, 1, 1" Size="6, 6, 6" Tessellation="1" />
          </Shapes>
        </Scene>
        """;

    private const int Width = 160;
    private const int Height = 90;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private MainGraphicsDevice _main;
    private IGraphicsDevice _device;
    private string _folder;
    private Process _host;
    private Task<string> _errors;
    private RenderTargetPanel _panel;
    private RemoteUniverse _universe;

    [SetUp]
    public void StartHost()
    {
        _main = MainGraphicsDevice.Create(new GraphicsDeviceFactory(), GraphicsSettings.CreateDefault(), "RemoteTests", true);
        _device = _main.CreateRenderDevice();
        _folder = Path.Combine(Path.GetTempPath(), "adamantium-remote-universe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "Cube.ascene"), Scene.Replace("        ", ""));

        var pipeName = "adamantium-remote-universe-" + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Adamantium.UniverseHost.dll"));
        start.ArgumentList.Add(pipeName);
        _host = Process.Start(start)!;
        _errors = _host.StandardError.ReadToEndAsync();
        _ = _host.StandardOutput.ReadToEndAsync();

        _panel = new RenderTargetPanel { Width = Width, Height = Height, Background = Brushes.Gray };
        var window = new Window { Width = 400, Height = 300, Content = _panel };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        _universe = new RemoteUniverse(_panel, _main.GraphicsAdapter.DeviceUuid);
        using var connecting = new CancellationTokenSource(Patience);
        _universe.Attach(UniverseHostConnection.ConnectAsync(pipeName, _universe.Viewer, connecting.Token)
            .GetAwaiter().GetResult());
    }

    [TearDown]
    public void StopHost()
    {
        _universe?.Dispose();
        if (_host is { HasExited: false })
        {
            _host.WaitForExit(Patience);
        }

        if (_host is { HasExited: false })
        {
            _host.Kill();
        }

        _host?.Dispose();
        _main?.Dispose();
        Directory.Delete(_folder, true);
    }

    [TestCase(FrameSharing.Auto)]
    [TestCase(FrameSharing.HostMemory)]
    public async Task TheHostsFrames_AreShownInThePanel_AndNewerOnesComeAsItTakesThem(FrameSharing sharing)
    {
        await _universe.LaunchAsync(new UniverseLaunch
        {
            ScenePath = Path.Combine(_folder, "Cube.ascene"),
            FrameSharing = sharing
        }, new CancellationTokenSource(Patience).Token);
        Assert.That(SpinWait.SpinUntil(() => _panel.Source != null, Patience), Is.True, "a surface bound to the panel");
        var descriptor = _panel.Source;

        var parameters = new PresentationParameters(PresenterType.RenderTarget, Width, Height, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(_device, parameters, "remote_universe_viewer");
        using var surface = SharedSurface.Import(_device, descriptor);
        var brightness = 0;
        var waited = Stopwatch.StartNew();
        while (brightness <= 60 && waited.Elapsed < Patience)
        {
            Take(presenter, surface);
            brightness = surface.LatchedCopy is { } frame ? CenterBrightness(frame) : 0;
        }

        var lit = descriptor.Submitted;
        waited.Restart();
        while (descriptor.Submitted <= lit + 3 && waited.Elapsed < Patience)
        {
            Take(presenter, surface);
        }

        var later = descriptor.Submitted;
        await _universe.StopAsync(CancellationToken.None);
        var ended = _universe.Ended.Wait(Patience);
        _universe.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.Width, Is.EqualTo((uint)Math.Round(_panel.ActualWidth * _universe.PixelsPerPoint)),
                "drawn at the panel's size");
            Assert.That(brightness, Is.GreaterThan(60), "the lit cube in the middle of the frame the panel took");
            Assert.That(later, Is.GreaterThan(lit + 3), "newer frames came as the panel took them");
            Assert.That(ended, Is.True, "told the universe stopped");
            Assert.That(_panel.Source, Is.Null, "the panel let go of the surface");
            Assert.That(_host.WaitForExit(Patience), Is.True, "the host exits once the viewer let go");
            Assert.That(_host.ExitCode, Is.EqualTo(0), _errors.Result);
        });
    }

    // One frame of the panel's, as its renderer takes the newest frame of the host's.
    private void Take(GraphicsPresenter presenter, SharedSurface surface)
    {
        _device.SetRenderTargets(presenter.RenderTarget);
        _device.SetDepthBuffer(presenter.DepthBuffer);
        _device.Presenter = presenter;
        Assert.That(_device.BeginDraw(beforeRenderPass: _ => surface.LatchCopy(_device)), Is.True);
        _device.EndDraw();
        _device.Submit();
        presenter.Present();
        _device.FrameEnded();
        _device.DeviceWaitIdle();
    }

    private static int CenterBrightness(ITexture frame)
    {
        using var image = frame.ToImage(0, 0);
        var pixel = new byte[4];
        var width = (int)frame.Width;
        Marshal.Copy(image.DataPointer + ((int)frame.Height / 2 * width + width / 2) * 4, pixel, 0, 4);
        return pixel[0] + pixel[1] + pixel[2];
    }
}
