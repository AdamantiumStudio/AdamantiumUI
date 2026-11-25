using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>What an input method composes is shown in the text box after the caret until it ends - unless the system
/// shows it itself.</summary>
[TestFixture]
[Category("Gpu")]
public class TextCompositionRenderTests
{
    private const int Width = 160;
    private const int Height = 64;

    private sealed class FontResourceFactory : IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        private readonly Dictionary<IGraphicsDevice, FontRenderer> _renderers = new();

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();

        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice)
        {
            if (!_renderers.TryGetValue(graphicsDevice, out var renderer))
            {
                _renderers[graphicsDevice] = renderer = new FontRenderer(graphicsDevice);
            }

            return renderer;
        }
    }

    private OffscreenTestRenderer _renderer;
    private VisualRoot _root;
    private TextBox _box;
    private TextPresenter _presenter;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new FontResourceFactory()), Width, Height)
            { ClearColor = Colors.Black };
        _presenter = new TextPresenter();
        _box = new TextBox
        {
            Text = "ab",
            CaretIndex = 2,
            Foreground = Brushes.White,
            FontSize = 24,
            Template = new ControlTemplate(() =>
            {
                var result = new TemplateResult { RootComponent = _presenter };
                result.RegisterName("PART_TextPresenter", _presenter);
                return result;
            }),
        };
        var canvas = new Canvas();
        canvas.Children.Add(_box);
        _root = new VisualRoot(canvas, Width, Height);
    }

    [TearDown]
    public void TearDown() => _renderer.Dispose();

    [Test]
    public void WhatIsComposed_IsShownAfterTheCaret_AndGoesWithTheEnd()
    {
        var before = RightmostLit();
        Raise(Keyboard.TextCompositionStartedEvent, string.Empty);
        Raise(Keyboard.TextCompositionChangedEvent, "HHH");
        var composing = RightmostLit();
        var width = _presenter.DesiredSize.Width;
        Raise(Keyboard.TextCompositionEndedEvent, string.Empty);
        var after = RightmostLit();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.InRange(10, 60), "the text is drawn");
            Assert.That(composing, Is.GreaterThan(before + 30), "and what is composed after it");
            Assert.That(width, Is.GreaterThan(composing), "within the surface");
            Assert.That(after, Is.EqualTo(before));
            Assert.That(_box.Text, Is.EqualTo("ab"));
        });
    }

    [Test]
    public void WhatTheSystemShows_TheBoxDoesNot()
    {
        var before = RightmostLit();
        Raise(Keyboard.TextCompositionStartedEvent, string.Empty);
        Raise(Keyboard.TextCompositionChangedEvent, "HHH", isShownBySystem: true);

        Assert.That(RightmostLit(), Is.EqualTo(before));
    }

    private void Raise(RoutedEvent routedEvent, string text, bool isShownBySystem = false) =>
        _box.RaiseEvent(new TextCompositionEventArgs(text, text.Length)
        {
            RoutedEvent = routedEvent,
            IsShownBySystem = isShownBySystem
        });

    // Settles glyphs that land in the atlas over the first frames.
    private int RightmostLit()
    {
        for (var i = 0; i < 4; i++)
        {
            ((IMeasurableComponent)_root).Measure(new Size<double>(Width, Height));
            ((IMeasurableComponent)_root).Arrange(new Rect(0, 0, Width, Height));
            LayoutManager.For(_root).ExecuteLayoutPass();
            Assert.That(_renderer.RenderFrame(_root), Is.True);
            RenderDirty.Clear();
        }

        using var image = _renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, pixels, 0, pixels.Length);
        var right = -1;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 64)
            {
                right = Math.Max(right, i / 4 % Width);
            }
        }

        return right;
    }
}
