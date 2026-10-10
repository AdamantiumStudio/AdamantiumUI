using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering.Verification;

internal sealed class FrameVerifier : IRenderCacheObserver, IDisposable
{
    private const int PopupShadow = 64;
    private const int AdornerReach = 8;

    private const int Idle = 0;
    private const int Armed = 1;
    private const int Matched = 2;
    private const int Spoiled = 3;

    private readonly FrameVerification _settings;
    private readonly IRootVisualComponent _root;
    private readonly IGraphicsDevice _device;
    private readonly RenderUnitFactory _factory;
    private readonly RenderCache _reference;
    private readonly List<IUIComponent> _flat = [];
    private readonly HashSet<IUIComponent> _visited = [];
    private readonly Stack<(IUIComponent Node, bool Hidden)> _stack = new();
    private readonly List<Rect> _masks = [];

    private int _state;
    private long _frame;
    private int _recorded;
    private RenderPacket _armedPacket;
    private RecordSummary _summary;
    private Matrix4x4F _projection;
    private Rect[] _maskSnapshot = [];
    private GraphicsPresenter _target;
    private byte[] _previous;
    private int _previousWidth;
    private int _previousHeight;

    /// <summary>Draws the reference on the window's own device, once the window has presented.</summary>
    public FrameVerifier(FrameVerification settings, IRootVisualComponent root, IGraphicsDevice windowDevice, IResourceFactory resourceFactory)
    {
        _settings = settings;
        _root = root;
        _device = windowDevice;
        _factory = new RenderUnitFactory(_device, resourceFactory);
        _reference = new RenderCache(new DrawingContext(), _factory);
    }

    public void Recorded(RenderPacket packet)
    {
        _frame++;
        var state = Volatile.Read(ref _state);
        if (state >= Matched || state == Armed && Interlocked.Exchange(ref _armedPacket, null) == null)
        {
            return;
        }

        if (packet.Kind == RenderBuildKind.Full || ++_recorded % Math.Max(1, _settings.Every) != 0)
        {
            return;
        }

        try
        {
            var summary = new RecordSummary(_frame, packet);
            Flatten();
            CollectMasks();
            _projection = packet.ProjectionMatrix;
            _reference.RecordComponents(_flat, _projection, readOnly: true);
            _summary = summary;
            Volatile.Write(ref _armedPacket, packet);
            Volatile.Write(ref _state, Armed);
        }
        catch (Exception e)
        {
            _settings.CountSkipped();
            _settings.Log($"frame {_frame}: the reference could not be recorded - {e}");
        }
    }

    public void Applied(RenderPacket packet)
    {
        var state = Volatile.Read(ref _state);
        if (state == Matched)
        {
            Volatile.Write(ref _state, Spoiled);
        }
        else if (state == Armed && Interlocked.CompareExchange(ref _armedPacket, null, packet) == packet)
        {
            Volatile.Write(ref _state, Matched);
        }
    }

    public void FramePresented(RenderCache live, GraphicsPresenter presenter, IRenderTarget drawn, IGraphicsDevice windowDevice,
        double scale)
    {
        var state = Volatile.Read(ref _state);
        if (state < Matched)
        {
            return;
        }

        try
        {
            _reference.ApplyLatestComponents();
            if (state == Spoiled)
            {
                _settings.CountSkipped();
                _settings.Log($"frame {_summary.Frame}: skipped - the window drew a later record than the reference");
            }
            else if (live.LastFrameWithheld)
            {
                _settings.CountSkipped();
                _settings.Log($"frame {_summary.Frame}: skipped - the window held the frame back for want of batch room");
            }
            else
            {
                Verify(live, presenter, drawn, windowDevice, scale);
            }
        }
        catch (Exception e)
        {
            _settings.CountSkipped();
            _settings.Log($"frame {_summary.Frame}: verifier failed - {e}");
        }
        finally
        {
            Volatile.Write(ref _state, Idle);
        }
    }

    public void Dispose()
    {
        _device.DeviceWaitIdle();
        _reference.DisposeUnits();
        _reference.DisposeDeviceResources();
        _target?.Dispose();
        _target = null;
        _factory.Dispose();
    }

    private void Verify(RenderCache live, GraphicsPresenter presenter, IRenderTarget drawn, IGraphicsDevice windowDevice,
        double scale)
    {
        var clear = windowDevice.ClearColor;
        windowDevice.DeviceWaitIdle();

        var width = (int)drawn.Width;
        var height = (int)drawn.Height;
        var format = drawn.ResolveTexture.SurfaceFormat;
        var livePixels = Read(drawn);
        var walkPixels = Read(DrawReference(presenter, drawn, scale, clear));

        var blueFirst = format.ToString().StartsWith("B8G8R8", StringComparison.OrdinalIgnoreCase);
        var comparison = FrameComparison.Compare(livePixels, walkPixels, width, height, MaskPixels(width, height, scale),
            Pack(clear, blueFirst));

        _settings.CountVerified();

        if (comparison.HasDifference)
        {
            _settings.CountMismatched();
            var line = $"frame {_summary.Frame}: {comparison.Different} px differ ({comparison.Extra} extra, {comparison.Missing} missing) " +
                       $"in [{comparison.Left},{comparison.Top}..{comparison.Right},{comparison.Bottom}] - {_summary.Kind}, drawn by {live.LastDrawPath}";
            if (_settings.TakeReport())
            {
                var folder = Path.Combine(_settings.SessionFolder, $"{_summary.Frame:D6}");
                FrameReport.Write(folder, _summary, live, _reference, comparison, livePixels, walkPixels, _previous,
                    _previousWidth, _previousHeight, format, blueFirst, scale, presenter.MSAALevel, _maskSnapshot,
                    _settings.TakeDump());
                line += $" -> {folder}";
            }

            _settings.Log(line);
        }

        _previous = livePixels;
        _previousWidth = width;
        _previousHeight = height;
    }

    private IRenderTarget DrawReference(GraphicsPresenter live, IRenderTarget drawn, double scale, Color clear)
    {
        var width = drawn.Width;
        var height = drawn.Height;
        var format = drawn.ResolveTexture.SurfaceFormat;
        if (_target == null || _target.Width != width || _target.Height != height || _target.MSAALevel != live.MSAALevel ||
            _target.SurfaceFormat != format)
        {
            _target?.Dispose();
            var parameters = new PresentationParameters(PresenterType.RenderTarget, width, height, IntPtr.Zero, live.MSAALevel)
            {
                ImageFormat = format,
                DepthFormat = live.DepthFormat
            };
            _target = GraphicsPresenter.Create(_device, parameters, "FrameVerifier_reference");
        }

        _reference.ProcessCommands(_projection, scale);

        var target = _target.RenderTarget;
        _device.ClearColor = clear;
        _device.SetRenderTargets(target);
        _device.SetDepthBuffer(_target.DepthBuffer);
        _device.MSAALevel = _target.MSAALevel;
        _device.Presenter = _target;

        var viewport = new Viewport { Width = width, Height = height, MinDepth = 0, MaxDepth = 1 };
        var scissor = new Rect2D { Offset = new Offset2D(), Extent = new Extent2D { Width = width, Height = height } };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!_device.BeginDraw(beforeRenderPass: _ => _reference.PreRender()))
            {
                throw new InvalidOperationException("the reference frame could not begin");
            }

            _device.SetViewports(viewport);
            _device.SetScissors(scissor);
            _reference.Render(_device, scissor);
            _device.EndDraw();
            _device.Submit();
            _target.Present();
            _device.FrameEnded();
            _device.DeviceWaitIdle();
            if (!_device.FrameWithheld)
            {
                break;
            }
        }

        return target;
    }

    private static byte[] Read(IRenderTarget target)
    {
        using var image = target.ResolveTexture.ReadbackToImage();
        var pixels = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, pixels, 0, pixels.Length);
        return pixels;
    }

    private static uint Pack(Color color, bool blueFirst) => blueFirst
        ? (uint)(color.B | color.G << 8 | color.R << 16 | color.A << 24)
        : (uint)(color.R | color.G << 8 | color.B << 16 | color.A << 24);

    private void Flatten()
    {
        _flat.Clear();
        _visited.Clear();
        _stack.Clear();
        _stack.Push((_root, false));

        while (_stack.Count > 0)
        {
            var (component, hiddenByAncestor) = _stack.Pop();
            if (component.Visibility == Visibility.Collapsed || !_visited.Add(component))
            {
                continue;
            }

            var hidden = hiddenByAncestor || component.Visibility != Visibility.Visible;
            if (!hidden)
            {
                _flat.Add(component);
            }

            RenderCache.PushChildrenInPaintOrder(_stack, component.VisualChildren, hidden);
        }
    }

    private void CollectMasks()
    {
        _masks.Clear();
        if (_root is IWindow window)
        {
            foreach (var popup in window.PopupRoots)
            {
                AddMask(popup, PopupShadow);
            }

            foreach (var adorner in window.Adorners)
            {
                AddMask(adorner, AdornerReach);
            }
        }

        _maskSnapshot = _masks.ToArray();
    }

    private void AddMask(IUIComponent component, double reach)
    {
        if (component == null || component.Visibility != Visibility.Visible)
        {
            return;
        }

        var size = component.RenderSize;
        var world = new Rect(0, 0, size.Width, size.Height).TransformToAABB(component.WorldTransform);
        _masks.Add(new Rect(world.X - reach, world.Y - reach, world.Width + reach * 2, world.Height + reach * 2));
    }

    private bool[] MaskPixels(int width, int height, double scale)
    {
        if (_maskSnapshot.Length == 0)
        {
            return null;
        }

        var mask = new bool[width * height];
        foreach (var rect in _maskSnapshot)
        {
            var left = Math.Max(0, (int)Math.Floor(rect.X * scale));
            var top = Math.Max(0, (int)Math.Floor(rect.Y * scale));
            var right = Math.Min(width, (int)Math.Ceiling((rect.X + rect.Width) * scale));
            var bottom = Math.Min(height, (int)Math.Ceiling((rect.Y + rect.Height) * scale));
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    mask[y * width + x] = true;
                }
            }
        }

        return mask;
    }
}
