using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The monitors (wl_output, with their logical place from xdg_output), as they come and go.</summary>
internal sealed class WaylandOutputs
{
    private readonly object sync = new();
    private readonly Dictionary<uint, Output> outputs = new();
    private ZxdgOutputManagerV1 manager;
    private IReadOnlyList<ScreenInfo> screens = [];

    /// <summary>The monitors connected now, in physical pixels: each at its logical place times the largest scale, so
    /// that monitors of different scales never overlap.</summary>
    public IReadOnlyList<ScreenInfo> Screens
    {
        get
        {
            lock (sync)
            {
                return screens;
            }
        }
    }

    /// <summary>All monitors as one rectangle, in physical pixels; empty before the first is known.</summary>
    public Rect VirtualScreen
    {
        get
        {
            var all = Screens;
            if (all.Count == 0)
            {
                return default;
            }

            var left = all.Min(s => s.Bounds.X);
            var top = all.Min(s => s.Bounds.Y);
            var right = all.Max(s => s.Bounds.X + s.Bounds.Width);
            var bottom = all.Max(s => s.Bounds.Y + s.Bounds.Height);
            return new Rect(left, top, right - left, bottom - top);
        }
    }

    public void SetManager(ZxdgOutputManagerV1 xdgOutputs)
    {
        lock (sync)
        {
            manager = xdgOutputs;
            foreach (var output in outputs.Values)
            {
                output.Follow(manager);
            }
        }
    }

    public void Add(uint name, WlOutput proxy)
    {
        var output = new Output(proxy, Update);
        lock (sync)
        {
            outputs[name] = output;
            if (manager != null)
            {
                output.Follow(manager);
            }
        }
    }

    /// <summary>Lets go of the monitor of this global, if it is one.</summary>
    public void Remove(uint name)
    {
        Output output;
        lock (sync)
        {
            if (!outputs.Remove(name, out output))
            {
                return;
            }
        }

        output.Release();
        Update();
    }

    private void Update()
    {
        lock (sync)
        {
            var known = outputs.Values.Where(o => o.Width > 0 && o.Height > 0).ToList();
            var primary = known.FirstOrDefault(o => o.LogicalX == 0 && o.LogicalY == 0) ?? known.FirstOrDefault();
            var placeScale = known.Count == 0 ? 1 : known.Max(o => o.Scale);
            screens = known.Select(o => o.ToScreen(o == primary, placeScale)).ToArray();
        }
    }

    private sealed class Output
    {
        private readonly WlOutput proxy;
        private readonly Action changed;
        private ZxdgOutputV1 xdgOutput;
        private int integerScale = 1;
        private int logicalWidth;
        private int logicalHeight;

        public Output(WlOutput proxy, Action changed)
        {
            this.proxy = proxy;
            this.changed = changed;
            proxy.ModeEvent += (flags, width, height, _) =>
            {
                if ((flags & WlOutput.Mode.Current) != 0)
                {
                    Width = width;
                    Height = height;
                }
            };
            proxy.Scale += factor => integerScale = Math.Max(1, factor);
            proxy.Name += value => Name = value;
            proxy.Done += changed.Invoke;
        }

        public string Name { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public int LogicalX { get; private set; }

        public int LogicalY { get; private set; }

        public void Follow(ZxdgOutputManagerV1 manager)
        {
            if (xdgOutput != null)
            {
                return;
            }

            xdgOutput = manager.GetXdgOutput(proxy);
            xdgOutput.LogicalPosition += (x, y) =>
            {
                LogicalX = x;
                LogicalY = y;
            };
            xdgOutput.LogicalSize += (width, height) =>
            {
                logicalWidth = width;
                logicalHeight = height;
            };
            xdgOutput.Done += changed.Invoke;
        }

        public double Scale => logicalWidth > 0 && logicalHeight > 0
            ? (double)Math.Max(Width, Height) / Math.Max(logicalWidth, logicalHeight)
            : integerScale;

        public ScreenInfo ToScreen(bool isPrimary, double placeScale)
        {
            var scale = Scale;
            var width = logicalWidth > 0 ? Math.Round(logicalWidth * scale) : Width;
            var height = logicalHeight > 0 ? Math.Round(logicalHeight * scale) : Height;
            var bounds = new Rect(LogicalX * placeScale, LogicalY * placeScale, width, height);
            return new ScreenInfo(Name ?? $"{LogicalX},{LogicalY}", bounds, bounds, scale, isPrimary);
        }

        public void Release()
        {
            xdgOutput?.Destroy();
            if (proxy.Version >= 3)
            {
                proxy.Release();
            }
        }
    }
}
