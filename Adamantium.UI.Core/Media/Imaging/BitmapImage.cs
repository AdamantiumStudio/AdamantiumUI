using System.Collections.Concurrent;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Core.Media.Imaging;

public sealed class BitmapImage : BitmapSource
{
   private static int _loadsInFlight;
   private static long _loadsFinished;
   private IRawBitmap _rawBitmap;
   // Initialized HERE, not per-constructor: the IRawBitmap overload chains to base() rather than this(), so it used to
   // leave both caches null and every frame fetch threw (swallowed -> an animated source silently froze on frame 0).
   private readonly Queue<BitmapFrame> _framesCache = new();
   private readonly Dictionary<uint, BitmapFrame> _indexToFrame = new();
   private readonly ConcurrentDictionary<(uint Mip, uint Quality), Lazy<BitmapFrame>> _levels = new();
   private volatile bool _released;
   private readonly Lock _loading = new();
   private int _loadGeneration;
   private volatile ITexture _framePalette;

   public BitmapImage()
   {
   }

   public BitmapImage(Uri uri) : this()
   {
      UriSource = uri;
   }

   public BitmapImage(
      UInt32 width,
      UInt32 height,
      double dpiX,
      double dpiY,
      SurfaceFormat format,
      byte[] pixels) : base(
      width,
      height,
      dpiX,
      dpiY,
      format,
      pixels)
   {
      // Its pixels are here already: ready the moment it exists, as one made from a decoded bitmap is.
      IsLoaded = true;
   }

   public BitmapImage(IRawBitmap bitmap) : base(
      bitmap.Width,
      bitmap.Height,
      1,
      1,
      bitmap.PixelFormat,
      bitmap.GetRawPixels(0))
   {
      _rawBitmap = bitmap;
      // Built from a bitmap that is ALREADY decoded, so it is ready the moment it exists. Without saying so the
      // renderer skips it forever (it refuses to draw a BitmapImage that has not finished loading), which only the URI
      // path used to announce - and an image handed over as bytes never drew at all.
      IsLoaded = true;
   }

   public static readonly AdamantiumProperty UriSourceProperty = AdamantiumProperty.Register(nameof(UriSource),
      typeof(Uri), typeof(BitmapImage), new PropertyMetadata(null, UriChangedCallback));

   private static void UriChangedCallback(AdamantiumComponent adamantiumAdamantiumComponent, AdamantiumPropertyChangedEventArgs e)
   {
      if (adamantiumAdamantiumComponent is BitmapImage bitmap)
      {
         bitmap.StartLoad((Uri)e.NewValue);
      }
   }

   public static readonly AdamantiumProperty ApplyColorProfileProperty = AdamantiumProperty.Register(
      nameof(ApplyColorProfile), typeof(bool), typeof(BitmapImage),
      new PropertyMetadata(false, ApplyColorProfileChangedCallback));

   private static void ApplyColorProfileChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
   {
      if (a is BitmapImage bitmap && bitmap.UriSource != null)
      {
         bitmap.StartLoad(bitmap.UriSource);
      }
   }

   /// <summary>Whether the ICC profile the file holds is applied as the file is read, its colors shown as the profile
   /// means them; off, as by default, the pixels are taken as sRGB as they are stored. A change reads the file again,
   /// as a new <see cref="UriSource"/> does, and an image showing the picture draws it anew (<see cref="Reloaded"/>).
   /// </summary>
   public bool ApplyColorProfile
   {
      get => GetValue<bool>(ApplyColorProfileProperty);
      set => SetValue(ApplyColorProfileProperty, value);
   }

   private void StartLoad(Uri uri)
   {
      var file = FileOf(uri, out var error);
      lock (_loading)
      {
         var generation = ++_loadGeneration;
         if (file == null)
         {
            LoadError = error;
            LoadTask = Task.CompletedTask;
            return;
         }

         // Load OFF the UI thread: decoding (especially every frame of an animated APNG/GIF) is heavy and froze the UI
         // while a view full of images was being built (a multi-second tab-switch hang). Consumers await LoadTask
         // before reading frames (see Image.ProcessImageSource); FrameCount/GetFrame report empty until it completes.
         var options = new BitmapLoadOptions { ApplyColorProfile = ApplyColorProfile };
         Interlocked.Increment(ref _loadsInFlight);
         LoadTask = Task.Run(() => Load(file, options, generation));
      }
   }

   private static string FileOf(Uri uri, out Exception error)
   {
      error = null;
      if (uri == null || string.IsNullOrWhiteSpace(uri.OriginalString))
      {
         return null;
      }

      if (uri.IsAbsoluteUri)
      {
         return uri.IsFile ? uri.LocalPath : null;
      }

      try
      {
         var path = uri.OriginalString;
         return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path));
      }
      catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
      {
         error = e;
         return null;
      }
   }
   
   public static readonly AdamantiumProperty FrameCacheLimitProperty = AdamantiumProperty.Register(nameof(FrameCacheLimit),
      typeof(uint), typeof(BitmapImage), new PropertyMetadata(10U, PropertyChangedCallback));

   private static void PropertyChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
   {
      if (a is BitmapImage bitmap)
      {
         // A lowered limit evicts the difference (subtract this way round: unsigned wraps).
         if (e.OldValue is uint oldLimit && e.NewValue is uint newLimit && newLimit < oldLimit)
         {
            bitmap.RemoveCacheItems(oldLimit - newLimit);
         }
      }
   }
   
   public static readonly AdamantiumProperty EnableFrameCacheProperty = AdamantiumProperty.Register(nameof(EnableFrameCache),
      typeof(bool), typeof(BitmapImage), new PropertyMetadata(true));

   public uint CurrentFrameIndex { get; private set; }

   public void ResetFrameIndex()
   {
      CurrentFrameIndex = StartFrame;
   }

   /// <summary>Pulls the playback cursor inside <see cref="StartFrame"/>..<see cref="EndFrame"/> - for when the range
   /// itself moves while the animation is running.</summary>
   public void ClampFrameIndex()
   {
      if (CurrentFrameIndex < StartFrame) CurrentFrameIndex = StartFrame;
      else if (CurrentFrameIndex > EndFrame) CurrentFrameIndex = EndFrame;
   }

   public bool EnableFrameCache
   {
      get => GetValue<bool>(EnableFrameCacheProperty);
      set => SetValue(EnableFrameCacheProperty, value);
   }

   public uint FrameCacheLimit
   {
      get => GetValue<uint>(FrameCacheLimitProperty);
      set => SetValue(FrameCacheLimitProperty, value);
   }

   [MarkupFile("png", "jpg", "jpeg", "gif", "bmp", "tga", "dds", "ico", "tif", "tiff", "jp2", "j2k")]
   public Uri UriSource
   {
      get => GetValue<Uri>(UriSourceProperty);
      set => SetValue(UriSourceProperty, value);
   }
   
   // Volatile: written on the background load thread AFTER the pixels/raw bitmap are filled, read on the render thread.
   // The release/acquire ordering guarantees that once a reader sees IsLoaded == true, those pixel writes are visible too
   // (so the OnRender "skip unloaded" guard never lets a half-populated bitmap reach texture creation).
   private volatile bool _isLoaded;
   public bool IsLoaded { get => _isLoaded; set => _isLoaded = value; }

   /// <summary>The background load kicked off when <see cref="UriSource"/> was set (null if the bitmap was built from raw
   /// data). Await it before reading frames so decoding never runs on the UI thread.</summary>
   public Task LoadTask { get; private set; }

   /// <summary>How many pictures are being read from their files right now, across the application.</summary>
   public static int LoadsInFlight => Volatile.Read(ref _loadsInFlight);

   /// <summary>How many pictures have finished reading their files, loaded or failed, counting up: a change says one
   /// has arrived since the count was last looked at.</summary>
   public static long LoadsFinished => Interlocked.Read(ref _loadsFinished);

   /// <summary>Completes when a URI-sourced bitmap has finished loading; completes immediately if there is no pending load.</summary>
   public Task EnsureLoadedAsync() => LoadTask ?? Task.CompletedTask;

   // Report empty until the (possibly still-running) background load has populated _rawBitmap, so an early read never NREs.
   public uint FrameCount => _rawBitmap?.FramesCount ?? 0;
   
   public uint StartFrame { get; set; }
   
   public uint EndFrame { get; set; }

   /// <summary>Whether an animation's frames are kept as LAYERS of one texture (the default) or as a texture per frame
   /// (the old path). Set ADAMANTIUM_FRAME_ARRAY=0 to fall back, so both can be compared from one build.</summary>
   public static bool UseFrameArrayTextures { get; set; } =
      Environment.GetEnvironmentVariable("ADAMANTIUM_FRAME_ARRAY") != "0";

   private volatile ITexture _frameArrayTexture;
   private int _frameArrayRequested;

   /// <summary>Every frame of this animation as the LAYERS of one texture: playing it is then choosing a layer - no
   /// upload, no allocation and no texture per frame (a 200-frame GIF used to mean 200 of them). Null until it is BUILT,
   /// which happens off the render thread - decoding every frame and uploading ~400 MB takes seconds, and doing that
   /// inside a frame is what froze the app on entering the tab. Callers draw the single-image path meanwhile.</summary>
   public ITexture FrameArrayTexture => _frameArrayTexture;

   /// <summary>The 256x1 palette of a <see cref="FrameArrayTexture"/> whose layers hold one palette index per pixel; null
   /// when the layers hold the full colors. Frames of at most 256 colors in all - most GIFs - take a quarter of the
   /// memory this way.</summary>
   public ITexture FramePalette => _framePalette;

   /// <summary>Starts building <see cref="FrameArrayTexture"/> if it isn't built or being built already. Returns at once.</summary>
   public void RequestFrameArrayTexture(IResourceFactory factory)
   {
      if (!UseFrameArrayTextures) return;
      if (_frameArrayTexture != null || _rawBitmap == null || FrameCount <= 1) return;
      // Frames larger or more than the device's arrays hold stay on the single-image path, shrunk there to fit.
      if (PixelWidth > factory.MaxTextureDimension || PixelHeight > factory.MaxTextureDimension
          || FrameCount > factory.MaxTextureArrayLayers) return;
      if (Interlocked.Exchange(ref _frameArrayRequested, 1) != 0) return;   // one build, however many images show it

      Task.Run(() =>
      {
         try
         {
            var description = new TextureDescription
            {
               Width = PixelWidth,
               Height = PixelHeight,
               Dimension = TextureDimension.Texture2D,
               Format = SurfaceLayout,
               Depth = 1,
               InitialLayout = ImageLayout.Undefined,
               ImageAspect = ImageAspectFlagBits.ColorBit,
               DesiredImageLayout = Layout,
               MipLevels = 1,
               ArrayLayers = FrameCount
            };

            byte[] palette = null;
            var indices = SurfaceLayout == SurfaceFormat.R8G8B8A8.UNorm ? IndexFrames(out palette) : null;
            if (indices != null)
            {
               _framePalette = factory.CreateTexture(new TextureDescription
               {
                  Width = IndexedFrames.PaletteSize,
                  Height = 1,
                  Dimension = TextureDimension.Texture2D,
                  Format = SurfaceLayout,
                  Depth = 1,
                  InitialLayout = ImageLayout.Undefined,
                  ImageAspect = ImageAspectFlagBits.ColorBit,
                  DesiredImageLayout = Layout,
                  MipLevels = 1,
                  ArrayLayers = 1
               }, palette);
               description.Format = SurfaceFormat.R8.UNorm;
               _frameArrayTexture = factory.CreateTextureArray(description, indices);
            }
            else
            {
               var layers = new byte[FrameCount][];
               for (uint i = 0; i < FrameCount; i++)
               {
                  layers[i] = _rawBitmap.GetRawPixels(i);
                  if (layers[i] == null) return;
               }

               _frameArrayTexture = factory.CreateTextureArray(description, layers);
            }

            // The frames now live on the GPU, so every decoded copy on the CPU is dead weight - and there are two of
            // them per frame (the RGBA pixels and, for a GIF, the palette-index stream), which for a 200-frame 960x540
            // animation is hundreds of megabytes. Playback from here on only moves an index; nothing reads pixels.
            var released = _rawBitmap;
            foreach (var frame in _indexToFrame) frame.Value?.Dispose();
            _indexToFrame.Clear();
            released.ReleaseDecodedFrames();
         }
         catch
         {
            // A failed build must leave the animation on the single-image path, not kill the loader thread - and not
            // hold a palette for an array that never came.
            if (_frameArrayTexture == null)
            {
               var palette = _framePalette;
               _framePalette = null;
               Retire(palette);
            }
         }
      });
   }

   private byte[][] IndexFrames(out byte[] palette)
   {
      palette = null;
      var indexer = new IndexedFrames();
      var indices = new byte[FrameCount][];
      var frame = new byte[_rawBitmap.TotalSizeInBytes];
      for (uint i = 0; i < FrameCount; i++)
      {
         _rawBitmap.CopyFrame(i, frame);
         indices[i] = indexer.TryIndex(frame);
         if (indices[i] == null)
         {
            return null;
         }
      }

      palette = indexer.Palette();
      return indices;
   }

   public BitmapFrame GetFrame(uint frameIndex)
   {
      if (_rawBitmap == null) return null;   // load still pending (async URI load) - nothing to hand back yet
      CurrentFrameIndex = frameIndex;
      var rawData = _rawBitmap.GetRawPixels(frameIndex);
      return new BitmapFrame(
         PixelWidth,
         PixelHeight,
         DpiXScale,
         DpiYScale,
         SurfaceLayout,
         rawData,
         frameIndex);
   }
   
   public BitmapFrame GetNextFrame()
   {
      if (CurrentFrameIndex >= EndFrame)
      {
         CurrentFrameIndex = StartFrame;
      }

      var frame = GetFrameFromCache(CurrentFrameIndex);

      CurrentFrameIndex++;
      return frame;
   }

   /// <summary>Moves the playback cursor one step and returns the frame it lands on - WITHOUT decoding anything. For an
   /// animation drawn from <see cref="FrameArrayTexture"/> the frame is a layer number, so asking the decoder for its
   /// pixels would rebuild hundreds of megabytes nobody reads.</summary>
   public uint AdvanceIndex(bool forward)
   {
      var shown = CurrentFrameIndex;

      if (forward)
      {
         if (CurrentFrameIndex >= EndFrame) shown = CurrentFrameIndex = StartFrame;
         CurrentFrameIndex++;
      }
      else
      {
         if (CurrentFrameIndex <= StartFrame) shown = CurrentFrameIndex = EndFrame;
         if (CurrentFrameIndex > 0) CurrentFrameIndex--;
      }

      return shown;
   }
   
   public BitmapFrame GetPreviousFrame()
   {
      if (CurrentFrameIndex <= StartFrame)
      {
         CurrentFrameIndex = EndFrame;
      }

      var frame = GetFrameFromCache(CurrentFrameIndex);
      
      CurrentFrameIndex--;
      return frame;
   }

   /// <summary>Decodes frames up to <paramref name="frameIndex"/> AHEAD of time. Despite the name it does the work
   /// SYNCHRONOUSLY on the calling thread and hands back a completed Task - awaiting it does not move anything off that
   /// thread. Playback does not need it (frames decode on demand); call it only to pay the cost somewhere deliberate.</summary>
   public Task DecodeFramesTillAsync(uint frameIndex)
   {
      if (frameIndex > FrameCount) frameIndex = FrameCount;
      
      for (uint i = 0; i < frameIndex; i++)
      {
         GetFrame(i);
      }

      return Task.CompletedTask;
   }

   // Empty until there IS a bitmap, exactly as FrameCount above - and for a bitmap that never arrived at all: a file
   // that has been moved or is not a picture leaves this null, and the read then threw on a THREAD-POOL thread out of
   // an async void, which is not an exception anybody can catch. It took the whole application down.
   public uint MipLevelsCount => _rawBitmap?.MipLevelsCount ?? 0;

   public bool HasMipLevels => MipLevelsCount > 0;

   /// <summary>The qualities the picture's data holds - a JPEG 2000's quality layers; 1 for any other loaded picture,
   /// 0 until it has loaded.</summary>
   public uint QualityLevelsCount => _rawBitmap?.QualityLevelsCount ?? 0;

   /// <summary>The picture at mip <paramref name="level"/>, made on first ask and kept by this image, so every image
   /// showing that level shares one texture. Null while the bitmap has not loaded or has no such level.</summary>
   public BitmapFrame GetMipLevel(uint level) => GetLevel(level, 0);

   /// <summary>Whether a level is decoded when first asked for, which can take a while - a JPEG 2000's - rather than
   /// read from what the picture already holds.</summary>
   public bool DecodesLevelsOnDemand => _rawBitmap?.DecodesLevelsOnDemand ?? false;

   /// <summary>The level <see cref="GetLevel"/> has made already, or null - nothing is decoded.</summary>
   public BitmapFrame FindLevel(uint mipLevel, uint qualityLevel) =>
      _levels.TryGetValue((mipLevel, qualityLevel), out var kept) && kept.IsValueCreated ? kept.Value : null;

   /// <summary>The picture at mip <paramref name="mipLevel"/> and quality <paramref name="qualityLevel"/> - 0 the
   /// full quality, each next one coarser - made once, however many ask at a time, and kept by this image. Null while
   /// the bitmap has not loaded, once it is released, or for a level it does not have. Where
   /// <see cref="DecodesLevelsOnDemand"/>, ask off the UI thread.</summary>
   public BitmapFrame GetLevel(uint mipLevel, uint qualityLevel)
   {
      if (_released || _rawBitmap == null)
      {
         return null;
      }

      var key = (mipLevel, qualityLevel);
      var level = _levels.GetOrAdd(key, k => new Lazy<BitmapFrame>(() => MakeLevel(k.Mip, k.Quality)));
      var frame = level.Value;
      if (frame == null)
      {
         _levels.TryRemove(new KeyValuePair<(uint, uint), Lazy<BitmapFrame>>(key, level));
         return null;
      }

      if (_released)
      {
         if (_levels.TryRemove(key, out var dropped))
         {
            dropped.Value?.Dispose();
         }

         return null;
      }

      return frame;
   }

   private BitmapFrame MakeLevel(uint mipLevel, uint qualityLevel)
   {
      var data = _rawBitmap?.GetLevelData(mipLevel, qualityLevel);
      return data == null
         ? null
         : new BitmapFrame(data.Description.Width, data.Description.Height, DpiXScale, DpiYScale, SurfaceLayout,
            data.RawPixels, mipLevel);
   }

   private async Task Load(string path, BitmapLoadOptions options, int generation)
   {
      Task latest = null;
      IRawBitmap read = null;
      Exception failed = null;

      // An unreadable file must not throw from this task, which nobody may observe; the image just stays unloaded.
      try
      {
         lock (_loading)
         {
            if (generation != _loadGeneration)
            {
               latest = LoadTask;
            }
         }

         if (latest == null)
         {
            read = BitmapLoader.Load(path, options);
         }
      }
      catch (Exception e)
      {
         failed = e;
      }
      finally
      {
         Interlocked.Increment(ref _loadsFinished);
         Interlocked.Decrement(ref _loadsInFlight);
      }

      latest ??= await Commit(read, failed, generation);
      if (latest != null)
      {
         await latest;
      }
   }

   private Task<Task> Commit(IRawBitmap read, Exception failed, int generation)
   {
      bool first;
      lock (_loading)
      {
         first = _rawBitmap == null;
      }

      var dispatcher = UIAppContext.Current?.Dispatcher;
      if (first || dispatcher is not { IsRunning: true })
      {
         return Task.FromResult(Apply(read, failed, generation));
      }

      var applied = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
      dispatcher.Post(() => applied.SetResult(Apply(read, failed, generation)));
      return applied.Task;
   }

   private Task Apply(IRawBitmap read, Exception failed, int generation)
   {
      bool replaced;
      lock (_loading)
      {
         if (generation != _loadGeneration)
         {
            return LoadTask;
         }

         replaced = _rawBitmap != null;
         if (replaced)
         {
            ForgetDecoded();
         }

         if (failed == null)
         {
            FillData(read);
            LoadError = null;
            IsLoaded = true;
         }
         else
         {
            LoadError = failed;
            IsLoaded = false;
         }
      }

      if (replaced)
      {
         Reloaded?.Invoke(this, EventArgs.Empty);
      }

      return null;
   }

   private void ForgetDecoded()
   {
      foreach (var frame in _indexToFrame.Values)
      {
         frame?.Dispose();
      }

      _indexToFrame.Clear();
      _framesCache.Clear();
      foreach (var level in _levels.Values)
      {
         if (level.IsValueCreated)
         {
            level.Value?.Dispose();
         }
      }

      _levels.Clear();
      Retire(_frameArrayTexture);
      _frameArrayTexture = null;
      Retire(_framePalette);
      _framePalette = null;
      Interlocked.Exchange(ref _frameArrayRequested, 0);
      _rawBitmap.ReleaseDecodedFrames();
      _rawBitmap = null;
      base.ReleaseUnmanagedResources();
   }

   /// <summary>Raised once a picture that had loaded has been read again - a new <see cref="UriSource"/>,
   /// <see cref="ApplyColorProfile"/> changed - and its pixels, frames and texture replaced; on the UI loop thread
   /// while the application runs.</summary>
   public event EventHandler Reloaded;

   /// <summary>Why the file did not load, where it did not. Null while it has not been tried, and null once it has
   /// loaded - a picture that failed is not a picture that is still coming, and something has to be able to tell the
   /// difference without watching the clock.</summary>
   public Exception LoadError { get; private set; }

   private void FillData(IRawBitmap rawBitmap)
   {
      PixelWidth = rawBitmap.Width;
      PixelHeight = rawBitmap.Height;
      SurfaceLayout = rawBitmap.PixelFormat;
      SetPixels(rawBitmap.GetRawPixels(0));
      _rawBitmap = rawBitmap;
   }

   /// <summary>The frame at <paramref name="index"/>, decoded on first ask and kept. Used while the frame array does not
   /// exist yet; once it does, playback is layer numbers and no frame is decoded at all.</summary>
   public BitmapFrame GetCachedFrame(uint index) => GetFrameFromCache(index);

   private BitmapFrame GetFrameFromCache(uint index)
   {
      //return GetFrame(index);
      if (!_indexToFrame.TryGetValue(index, out var frame))
      {
         frame = GetFrame(index);
         _indexToFrame[index] = frame;
         //AddToCache(frame);
      }

      // if (_indexToFrame.Count > FrameCacheLimit)
      // {
      //    RemoveCacheItems(FrameCacheLimit - (uint)_indexToFrame.Count);
      // }

      return frame;
   }
   
   protected override void ReleaseUnmanagedResources()
   {
      foreach (var bitmapFrame in _indexToFrame)
      {
         bitmapFrame.Value?.Dispose();
      }
      _indexToFrame.Clear();
      _released = true;
      foreach (var level in _levels.Values)
      {
         if (level.IsValueCreated)
         {
            level.Value?.Dispose();
         }
      }

      _levels.Clear();
      Retire(_frameArrayTexture);
      _frameArrayTexture = null;
      Retire(_framePalette);
      _framePalette = null;
      _rawBitmap?.ReleaseDecodedFrames();
      // ...and the base's own texture, which is where a still image's ENTIRE picture lives (77 MB for the 4984x3858 TGA).
      // Overriding this without calling it left that texture unreachable and unfreed: the only path to it was here.
      base.ReleaseUnmanagedResources();
   }

   private void AddToCache(BitmapFrame frame)
   {
      _framesCache.Enqueue(frame);
      if (_framesCache.Count > FrameCacheLimit)
      {
         var extraFrame = _framesCache.Dequeue();
         extraFrame.Dispose();
      }
   }

   private void RemoveCacheItems(uint number)
   {
      for (int i = 0; i < number; i++)
      {
         if (_framesCache.Count > 0)
         {
            var oldFrame = _framesCache.Dequeue();
            _indexToFrame.Remove(oldFrame.Index);
            oldFrame.Dispose();
         }
      }
   }
}