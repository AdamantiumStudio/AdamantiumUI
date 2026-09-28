using System.Reflection;
using System.Text.RegularExpressions;
using Adamantium.Core;
using Adamantium.Multiverse.Input;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Adorners;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using Adamantium.UI.EntityServices;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Universes;

namespace Adamantium.UI.Designer.Host;

/// <summary>A warm headless session: the first <see cref="Render"/> boots the engine as the previewed project's own
/// application; the device and window then persist, so each render of AUML text is cheap.</summary>
public sealed class DesignerSession : IDisposable
{
    private UIApplication _app;
    private IGraphicsDevice _device;
    private IGraphicsDeviceService _deviceService;
    private IUniverseService _universeService;

    // Why a file's views may not resolve what they would in their own application: the session booted as another one.
    private string _servicesNote;

    // The tab a click showed, per file and per tab control (by its place in the tree).
    private readonly Dictionary<(string Uri, string Place), int> _shownTabs = new();

    // One render service for the whole designer session: ONE device (the shared _device) + one renderer/presenter,
    // re-pointed/resized per previewed window (WindowRenderService.RenderHeadlessFrame). It drives the content
    // renderer AND the adorner processor (selection frames), exactly like the runtime path.
    private DesignerRenderService _renderService;

    // The last rendered tree, kept so a follow-up hittest request (designer click/hover) can map a point back to
    // the authored element and its markup position without re-rendering.
    private IWindow _lastWindow;
    private IReadOnlyDictionary<object, AumlSourceSpan> _lastSourceMap;
    // The element the hover frame currently decorates. Hover re-renders the whole scene, so we skip it while the cursor
    // stays over the same element (the common case) - only an element CHANGE warrants a new frame.
    private object _lastHoverElement;

    // The live preview session: the tree/renderer/target from the last Render, kept alive so the streaming "frame" op
    // (RenderNextFrame) advances and re-renders the SAME scene each tick instead of re-capturing a batch.
    private IWindow _liveWindow;
    private uint _liveTargetWidth;
    private uint _liveTargetHeight;
    private double _liveScale;
    private uint _liveDesignWidth;
    private uint _liveDesignHeight;

    // Hot reload: the AST the live tree was built from, the authored root instance, and the file it came from. An edit
    // to the SAME file reconciles this tree in place (keeping animations) instead of rebuilding it.
    private AumlAstObjectNode _lastAst;
    private object _liveAuthoredRoot;
    private string _lastUri;

    private const double DefaultWidth = 1280;
    private const double DefaultHeight = 720;

    // Render-target size cap so extreme zoom cannot exhaust GPU memory; the client upscales past it. Clamped to the
    // device's maxImageDimension2D.
    private const double PreferredMaxRenderDimension = 8192;
    private double _maxRenderDimension;

    public DesignerSession()
    {
        // Tell design-unsafe code (universe-hosting behaviors etc.) it is running in the previewer, so it stays dormant.
        Design.IsDesignMode = true;

        // Every preview is a one-shot frame: glyphs rasterized in the background would land after it was taken, and the
        // preview showed no text at all. Filled inline, as an off-screen bake does (see VisualRenderer).
        Adamantium.Graphics.Fonts.FontAtlasStore.SynchronousFill = true;

        // Every engine assembly of the host's own closure, not every dll in its folder: a leftover of an older build
        // would shadow the previewed project's fresh copy of the same assembly.
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];
        foreach (var dll in trusted)
        {
            if (!Path.GetFileName(dll).StartsWith("Adamantium", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try { Assembly.LoadFrom(dll); } catch { /* ignore unloadable */ }
        }
    }

    // Boots the engine on the first render, as the previewed project's own application: its services are what its views
    // and view-models resolve, and without them a view built from view-models (the gallery's tabs) came up empty.
    private void EnsureBooted(Assembly project)
    {
        var wanted = ApplicationTypeOf(project);
        if (_app != null)
        {
            _servicesNote = wanted != null && wanted != _app.GetType()
                ? $"previewed with the services of {_app.GetType().Name}: the designer was started for another project - restart it to preview with those of {wanted.Name}"
                : null;
            return;
        }

        _app = (UIApplication)Activator.CreateInstance(wanted ?? typeof(DesignerApplication));

        // Headless, so create the device explicitly. Vulkan validation is opt-in via ADAMANTIUM_DESIGNER_GRAPHICS_DEBUG=1
        // and reports into the host log.
        _deviceService = _app.Container.Resolve<IGraphicsDeviceService>();
        _deviceService.IsInDebugMode = Environment.GetEnvironmentVariable("ADAMANTIUM_DESIGNER_GRAPHICS_DEBUG") == "1";
        _deviceService.CreateMainDevice("Designer");

        // A preview reads no gamepad, and the application's own backend would start GameInput in the designer's process.
        _app.Container.RegisterSingleton<IGamepadBackend, NoGamepadBackend>();
        _app.InitializeWithoutRunning();
        _universeService = _app.Container.IsRegistered<IUniverseService>() ? _app.Container.Resolve<IUniverseService>() : null;

        _device = _app.GraphicsContext.CreateGraphicsDevice();

        _maxRenderDimension = Math.Min(
            PreferredMaxRenderDimension,
            _device.Adapter.AdapterProperties.Limits.MaxImageDimension2D);
    }

    // The application a project runs as: the concrete UIApplication its assembly declares. Null for a library of views.
    private static Type ApplicationTypeOf(Assembly project) => project == null
        ? null
        : SafeGetTypes(project).FirstOrDefault(t =>
            typeof(UIApplication).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null);

    /// <summary>
    /// Loads the AUML text into a live tree, lays it out at the window's design size and renders it to
    /// <paramref name="outPath"/> at design size × <paramref name="scale"/>. The window is always laid out at its
    /// design size (declared Width/Height, else <paramref name="requestWidth"/>/<paramref name="requestHeight"/>,
    /// else a default), and only the render target is scaled - so zooming re-rasterises the same layout crisply
    /// rather than reflowing it.
    /// </summary>
    public RenderResult Render(string aumlText, uint? requestWidth, uint? requestHeight, double scale, string outPath, string? aumlSourcePath = null, bool live = false)
    {
        // Live preview lets design-mode animations run (a one-shot render keeps them settled). Set before layout below.
        Design.IsLivePreview = live;

        // Relative asset paths (e.g. <Image Source="Textures/foo.tga">) are loaded against the process working
        // directory, exactly as in the running app (which runs from its output dir). Point the CWD at the edited
        // file's project root so the live designer loads those assets straight from the project source.
        var assetRoot = ResolveAssetRoot(aumlSourcePath);
        if (assetRoot != null) Directory.SetCurrentDirectory(assetRoot);

        // Load the edited file's own project assembly so its types (clr-namespace: controls, behaviors) resolve, not just
        // engine types, and the first render boots the engine as that project's application.
        EnsureBooted(LoadProjectAssembly(aumlSourcePath));

        // Hot reload: an edit to a file we already have live -> reconcile the EXISTING tree in place (changed properties
        // re-applied so transitions ease, added/removed children spliced in) instead of rebuilding it. The animation
        // clocks are NOT reset, so everything that was running keeps running. Only a full (first) build resets them.
        if (live && _lastWindow != null && _lastAst != null && _liveAuthoredRoot != null
            && string.Equals(aumlSourcePath, _lastUri, StringComparison.Ordinal))
        {
            var rec = AumlLoader.Reconcile(_liveAuthoredRoot, _lastAst, aumlText,
                AppDomain.CurrentDomain.GetAssemblies(),
                t => typeof(IWindow).IsAssignableFrom(t) ? typeof(VirtualWindow) : t);
            if (rec.Reconciled)
            {
                _lastAst = rec.Ast;
                var win = _lastWindow;
                var src = (_liveAuthoredRoot as IMeasurableComponent) ?? (win as IMeasurableComponent);
                var dw = ResolveDimension(src?.Width, requestWidth, DefaultWidth);
                var dh = ResolveDimension(src?.Height, requestHeight, DefaultHeight);
                win.ClientWidth = dw;
                win.ClientHeight = dh;
                win.Update(_app.ThemeManager, new AppTime());
                (dw, dh) = ShrinkToContent(win, _liveAuthoredRoot, src, dw, dh);
                // Reconcile keeps the SAME control instances (same RenderIds), so their cached render units are still
                // valid - do NOT reset the cache (that would dispose every unit, and unchanged/clean controls record
                // nothing to rebuild them -> white screen). The changed control re-renders itself via its invalidation.
                return RenderTail(win, dw, dh, scale, outPath, live, rec.Diagnostics, resetCache: false);
            }
            // reconcile declined (root element type changed) -> fall through to a full rebuild
        }

        // Full (re)build: drop any animations/images the previous tree left in the shared static clocks so the fresh
        // tree starts clean and a later tick never advances stale animations bound to now-discarded controls.
        AnimationManager.Reset();
        DesignTimeMediaClock.Reset();

        var load = AumlLoader.Load(
            aumlText,
            AppDomain.CurrentDomain.GetAssemblies(),
            t => typeof(IWindow).IsAssignableFrom(t) ? typeof(VirtualWindow) : t);

        // The root may be a Window, or any visual control (a View / UserControl-style root, a panel, a single
        // control). Non-window roots are hosted in a design-time VirtualWindow so the designer previews them too,
        // the way WPF previews a UserControl. A template is shown built; only themes, styles and resources have no
        // look of their own.
        IWindow window;
        object authoredVisual;
        switch (load.Root)
        {
            case IWindow w:
                // Shown as the window being worked in: the caption in its focused look.
                (w as WindowBase)?.SetIsActive(true);
                window = w;
                authoredVisual = w;
                break;
            case IUIComponent control:
                window = CaptionlessHost(control);
                authoredVisual = control;
                break;
            case ControlTemplate controlTemplate:
                // On an instance of the control it is for, so its TemplateBindings and triggers have a control to follow.
                var templated = TemplatedHostFor(controlTemplate);
                window = CaptionlessHost(templated);
                authoredVisual = templated;
                break;
            case UiTemplate template:
                // A data or items-panel template: its content, against the design data context when there is one.
                if (template.Build(null)?.RootComponent is not { } content)
                    return RenderResult.Fail($"{template.GetType().Name} has no content to preview", load.Diagnostics);
                window = CaptionlessHost(content);
                authoredVisual = content;
                break;
            case null:
                // The markup did not build: say why, not that the file is of a kind that can't be previewed.
                var reason = load.Diagnostics.LastOrDefault(d => d.Contains("error", StringComparison.OrdinalIgnoreCase))
                             ?? load.Diagnostics.LastOrDefault() ?? "unknown error";
                return RenderResult.Fail($"the markup could not be built: {reason}", load.Diagnostics);
            default:
                return RenderResult.Fail(IsThemeStyleOrResource(load.Root)
                    ? $"{load.Root.GetType().Name} is a theme, style or resource set: it has no look of its own to preview"
                    : $"{load.Root.GetType().Name} is not a visual element: nothing to preview", load.Diagnostics);
        }

        var sizeSource = authoredVisual as IMeasurableComponent;
        window.AttachContextAndInitialize(_app.UIContext);

        // Design-time DataContext (x:ViewModel, opt-in via x:CreateInDesignTime="True"), built through the application's
        // services so {Binding} paths resolve in the preview.
        var designContext = CreateDesignDataContext(aumlText, load.Diagnostics);

        var designWidth = ResolveDimension(sizeSource?.Width, requestWidth, DefaultWidth);
        var designHeight = ResolveDimension(sizeSource?.Height, requestHeight, DefaultHeight);

        window.ClientWidth = designWidth;
        window.ClientHeight = designHeight;

        // Layout (Measure/Arrange + theme) at the design size - geometry only, no native window. Done first so the
        // templates expand before the DataContext is assigned, then re-run so bound values participate in layout.
        window.Update(_app.ThemeManager, new AppTime());

        if (designContext != null && authoredVisual is IFundamentalUIComponent rootComponent)
        {
            rootComponent.DataContext = designContext;
            window.Update(_app.ThemeManager, new AppTime());
        }

        if (ShowTabsShownBefore(window, aumlSourcePath))
        {
            window.Update(_app.ThemeManager, new AppTime());
        }

        (designWidth, designHeight) = ShrinkToContent(window, authoredVisual, sizeSource, designWidth, designHeight);

        // Keep the laid-out tree + source map + AST for a follow-up hittest and for reconciling the next edit. A template
        // is rebuilt on every edit: its content is not a live tree the reconciler can patch.
        _lastWindow = window;
        _lastSourceMap = load.SourceMap;
        _lastAst = load.Ast;
        _liveAuthoredRoot = load.Root is UiTemplate ? null : load.Root;
        _lastUri = aumlSourcePath;

        return RenderTail(window, designWidth, designHeight, scale, outPath, live, load.Diagnostics, resetCache: true);
    }

    // What is not a window has no caption of its own: it is shown in a window without one.
    private static VirtualWindow CaptionlessHost(IUIComponent content) => new() { Content = content, UseCustomChrome = false };

    // A click on a tab header shows that tab, as it does in the running application: a page behind another tab is looked
    // at in the preview too. The markup is left alone. True when the click landed on one.
    private bool ShowTabAt(double x, double y)
    {
        if (_lastWindow is not IUIComponent root) return false;

        for (var node = root.GetVisualsAt(new Vector2(x, y)).FirstOrDefault(); node != null; node = node.VisualParent)
        {
            if (node is not TabItem tab) continue;

            tab.SpringLoad();
            if (tab.GetVisualAncestors().OfType<TabControl>().FirstOrDefault() is { } tabs)
            {
                _shownTabs[(_lastUri, PlaceOf(tabs))] = tabs.SelectedIndex;
            }

            return true;
        }

        return false;
    }

    // A rebuild of the same file shows the tabs a click showed before, not the ones its view-models start on.
    private bool ShowTabsShownBefore(IWindow window, string uri)
    {
        var shown = false;
        foreach (var tabs in DescendantsOf<TabControl>((IUIComponent)window))
        {
            if (_shownTabs.TryGetValue((uri, PlaceOf(tabs)), out var index) && index < tabs.Items.Count && tabs.SelectedIndex != index)
            {
                tabs.SelectedIndex = index;
                shown = true;
            }
        }

        return shown;
    }

    // Where an element sits in its tree, as child indices from the root: the same markup builds the same places.
    private static string PlaceOf(IUIComponent element)
    {
        var steps = new List<int>();
        for (var at = element; at.VisualParent is { } parent; at = parent)
        {
            steps.Add(parent.VisualChildren.TakeWhile(child => !ReferenceEquals(child, at)).Count());
        }

        steps.Reverse();
        return string.Join('/', steps);
    }

    private static IEnumerable<T> DescendantsOf<T>(IUIComponent root) where T : class
    {
        foreach (var child in root.VisualChildren)
        {
            if (child is T found)
            {
                yield return found;
            }

            foreach (var deeper in DescendantsOf<T>(child))
            {
                yield return deeper;
            }
        }
    }

    // The control a lone ControlTemplate is shown on: its TargetType when that is a creatable templated control, else a
    // ContentControl.
    private static TemplatedUIComponent TemplatedHostFor(ControlTemplate template)
    {
        var type = template.TargetType;
        if (type == null || type.IsAbstract || typeof(IWindow).IsAssignableFrom(type)
            || !typeof(TemplatedUIComponent).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
        {
            type = typeof(ContentControl);
        }

        var host = (TemplatedUIComponent)Activator.CreateInstance(type);
        host.Template = template;
        return host;
    }

    // What the markup kinds with no visual of their own implement: themes, theme variants, resource dictionaries, style
    // sets (the same interfaces the markup compiler reads an entity type from), and a lone Style.
    private static bool IsThemeStyleOrResource(object root) =>
        root is Style || root.GetType().GetInterfaces().Any(i => i.Name is "ITheme" or "IThemeVariant" or "IResourceDictionary" or "IStyleSet");

    // A hosted control with no declared size: shrink the design canvas to its natural (content) size so the preview
    // fits the control instead of a full default window. Windows and explicitly-sized controls keep their size.
    private (double Width, double Height) ShrinkToContent(IWindow window, object authoredRoot, IMeasurableComponent sizeSource, double designWidth, double designHeight)
    {
        if (authoredRoot is IWindow || sizeSource is not { } s || !double.IsNaN(s.Width) || !double.IsNaN(s.Height))
            return (designWidth, designHeight);

        var desired = s.DesiredSize;
        if (desired.Width >= 1 && desired.Height >= 1 && (desired.Width < designWidth || desired.Height < designHeight))
        {
            designWidth = Math.Min(designWidth, desired.Width);
            designHeight = Math.Min(designHeight, desired.Height);
            window.ClientWidth = designWidth;
            window.ClientHeight = designHeight;
            window.Update(_app.ThemeManager, new AppTime());
        }
        return (designWidth, designHeight);
    }

    // The shared rendering tail: drives any design-time universe, picks the render scale/target, renders the current frame
    // and remembers the live session so the streaming "frame" op can advance it. Used by both the full build and the
    // hot-reload reconcile path.
    private RenderResult RenderTail(IWindow window, double designWidth, double designHeight, double scale, string outPath, bool live, List<string> diagnostics, bool resetCache)
    {
        if (_servicesNote != null) diagnostics.Add(_servicesNote);

        // Design-time universe preview: a design-aware behavior (e.g. DemoUniverseBehavior) created+bound a universe to
        // its RenderTargetPanel while the tree was built. Drive a short snapshot so the universe loads content and
        // publishes a frame to the panel; the content render path composites that below. No-op when none is hosted.
        DriveDesignTimeUniverses();

        if (scale <= 0) scale = 1.0;
        var maxScale = Math.Max(1.0, Math.Min(_maxRenderDimension / designWidth, _maxRenderDimension / designHeight));
        var renderScale = Math.Min(scale, maxScale);
        // TRUNCATE (not round): the renderer sizes the presenter/render-target with (uint)(ClientSize * scale) =
        // truncation, so the frame file is truncate(design*scale) px. Reporting a rounded (1px larger) size makes the
        // editor read width*height*4 bytes from a smaller file -> the read underruns -> a bogus "render failed". The
        // reported size MUST equal the bytes actually written.
        var targetWidth = (uint)Math.Max(1.0, Math.Floor(designWidth * renderScale));
        var targetHeight = (uint)Math.Max(1.0, Math.Floor(designHeight * renderScale));

        var designW = (uint)Math.Round(designWidth);
        var designH = (uint)Math.Round(designHeight);

        // MSAA x4 for crisp edges. Set before the service binds its presenter (the sample count is fixed at presenter
        // creation, so every previewed window uses the same).
        // MSAA off: fills + strokes are analytically antialiased (GPU coverage fringe), matching WindowBase's default.
        window.MSAALevel = MSAALevel.None;

        var service = GetRenderService(window);
        // Only a full build needs to drop the previous tree's units (new instances/RenderIds). On a reconcile the
        // instances are unchanged, so their cached units stay valid - resetting here would white out every clean control.
        if (resetCache) service.ResetFrameCache();

        // One device + one presenter for the whole session: the service re-points/resizes to this window + scale (no
        // recreation), renders content + adorner overlays (its processor), and the frame is read from the resolve texture.
        if (!service.RenderHeadlessFrame(window, renderScale, new AppTime()))
            return RenderResult.Fail("render failed", diagnostics);
        service.SaveFrameRaw(outPath);   // raw B8G8R8A8 - no PNG encode (the editor converts the bytes)

        _liveWindow = window;
        _liveTargetWidth = targetWidth;
        _liveTargetHeight = targetHeight;
        _liveScale = renderScale;
        _liveDesignWidth = designW;
        _liveDesignHeight = designH;

        var animating = live && (AnimationManager.HasActiveAnimations || DesignTimeMediaClock.HasActiveMedia);
        return RenderResult.Ok([outPath], diagnostics, targetWidth, targetHeight, renderScale, designW, designH, animating);
    }

    /// <summary>
    /// Advances the live preview by one frame: ticks the design-time animation clocks, re-lays-out and re-renders the
    /// SAME tree captured by the last <see cref="Render"/>, and writes the frame. The client calls this ~60fps while
    /// <see cref="RenderResult.Animating"/> is true to play animations live (no batch capture). Returns Animating=false
    /// once nothing is left to animate, so the client stops streaming.
    /// </summary>
    public RenderResult RenderNextFrame(string outPath)
    {
        if (_lastWindow == null || _renderService == null || _liveWindow == null)
            return RenderResult.Fail("no live session to advance", null);

        const double dt = 1.0 / 60.0;
        AnimationManager.Tick(dt);
        DesignTimeMediaClock.Tick(dt);                     // advance animated images by virtual time
        _lastWindow.Update(_app.ThemeManager, new AppTime());   // re-run layout for any animation-driven size change

        if (!_renderService.RenderHeadlessFrame(_liveWindow, _liveScale, new AppTime()))
            return RenderResult.Fail("render failed", null);
        _renderService.SaveFrameRaw(outPath);

        var animating = AnimationManager.HasActiveAnimations || DesignTimeMediaClock.HasActiveMedia;
        return RenderResult.Ok([outPath], null, _liveTargetWidth, _liveTargetHeight, _liveScale,
            _liveDesignWidth, _liveDesignHeight, animating);
    }

    /// <summary>
    /// The directory relative asset paths resolve against for the file being previewed: the nearest .csproj ancestor
    /// (the project root, matching how the app finds assets relative to its output root), else the file's own folder.
    /// Accepts a plain path or a file:// URI; null/blank yields null (CWD left unchanged).
    /// </summary>
    private static string? ResolveAssetRoot(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        string path;
        try { path = sourcePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(sourcePath).LocalPath : sourcePath; }
        catch { path = sourcePath; }

        string? dir;
        try { dir = Path.GetDirectoryName(Path.GetFullPath(path)); }
        catch { return null; }
        if (dir == null) return null;

        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
            if (d.GetFiles("*.csproj").Length > 0) return d.FullName;

        return Directory.Exists(dir) ? dir : null;
    }

    // Project assemblies loaded this session: load each once so the warm host doesn't add duplicate type copies.
    private static readonly Dictionary<string, Assembly> _loadedProjectAssemblies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads the previewed file's own project assembly from its build output, so the designer can resolve the
    /// project's own types (<c>clr-namespace:</c> controls, behaviors) - not only engine assemblies - and returns it.
    /// Best-effort: no .csproj ancestor, an unbuilt project or an unloadable dll return null and just leave those types
    /// unresolved, as before.
    /// </summary>
    private static Assembly LoadProjectAssembly(string? aumlSourcePath)
    {
        var projectDir = ResolveAssetRoot(aumlSourcePath);
        if (projectDir == null) return null;
        var csproj = Directory.GetFiles(projectDir, "*.csproj").FirstOrDefault();
        if (csproj == null) return null;   // ResolveAssetRoot fell back to a non-project folder

        var dll = FindProjectAssembly(csproj);
        if (dll == null) return null;

        // Resolve missing assemblies from the project's output dir (e.g. Adamantium.MVVM); otherwise reflection silently
        // drops view-model types.
        _projectOutputDir = Path.GetDirectoryName(dll);
        lock (_loadedProjectAssemblies)
        {
            if (!_resolverRegistered)
            {
                AppDomain.CurrentDomain.AssemblyResolve += ResolveFromProjectOutput;
                _resolverRegistered = true;
            }
            if (_loadedProjectAssemblies.TryGetValue(dll, out var loaded)) return loaded;   // already loaded this session
        }

        // Load from a byte copy, not Assembly.LoadFrom: LoadFrom keeps the .dll file locked, which would stop the
        // user rebuilding their project while this warm preview host has it open. Loaded once per session (a rebuild
        // is picked up after a host restart; full hot-reload is a separate, deferred workstream).
        Assembly assembly = null;
        try { assembly = Assembly.Load(File.ReadAllBytes(dll)); } catch { /* ignore unloadable */ }
        lock (_loadedProjectAssemblies) _loadedProjectAssemblies[dll] = assembly;
        return assembly;
    }

    // Output dir of the last previewed project; probed by ResolveFromProjectOutput for the project's own dependencies.
    private static string _projectOutputDir;
    private static bool _resolverRegistered;

    /// <summary>Resolves an assembly the host can't find on its own probing path from the previewed project's output
    /// directory (where the project's dependencies, e.g. Adamantium.MVVM, sit). Loaded by bytes so the file isn't locked.</summary>
    private static Assembly ResolveFromProjectOutput(object sender, ResolveEventArgs args)
    {
        var dir = _projectOutputDir;
        if (dir == null) return null;
        var path = Path.Combine(dir, new AssemblyName(args.Name).Name + ".dll");
        if (!File.Exists(path)) return null;
        try { return Assembly.Load(File.ReadAllBytes(path)); } catch { return null; }
    }

    // The project's compiled assembly, looked up in BaseOutputPath, bin and an artifacts/bin above it; the newest match
    // wins over stale TFM builds.
    private static string? FindProjectAssembly(string csprojPath)
    {
        var dllName = Path.GetFileNameWithoutExtension(csprojPath) + ".dll";

        foreach (var binBase in CandidateOutputRoots(csprojPath))
        {
            if (!Directory.Exists(binBase)) continue;
            var dll = Directory.EnumerateFiles(binBase, dllName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (dll != null) return dll;
        }
        return null;
    }

    private static IEnumerable<string> CandidateOutputRoots(string csprojPath)
    {
        var projectDir = Path.GetDirectoryName(csprojPath)!;

        // 1. An explicit <BaseOutputPath> in the csproj.
        string baseOutput = null;
        try { baseOutput = System.Xml.Linq.XDocument.Load(csprojPath).Descendants("BaseOutputPath").FirstOrDefault()?.Value?.Trim(); }
        catch { /* unreadable csproj - skip */ }
        if (!string.IsNullOrEmpty(baseOutput))
            yield return Path.GetFullPath(Path.Combine(projectDir, baseOutput));

        // 2. The conventional per-project bin.
        yield return Path.Combine(projectDir, "bin");

        // 3. A consolidated artifacts/bin (this engine's layout: a root Directory.Build.props redirects every
        //    project's OutputPath there, so the project has no local bin). Walk up from the project to find it.
        for (var dir = new DirectoryInfo(projectDir); dir != null; dir = dir.Parent)
        {
            var artifactsBin = Path.Combine(dir.FullName, "artifacts", "bin");
            if (Directory.Exists(artifactsBin)) { yield return artifactsBin; break; }
        }
    }

    /// <summary>
    /// Builds the markup's view-model (<c>x:ViewModel="prefix:Type"</c>) the way the application does, through its
    /// services, so the preview shows real data through its bindings. Opt-in, as for every view: only with
    /// <c>x:CreateInDesignTime="True"</c>, because a preview runs the view-model's code. Returns null when not opted in,
    /// when there is no x:ViewModel, or when it cannot be built (a diagnostic says why).
    /// </summary>
    private object CreateDesignDataContext(string aumlText, List<string> diagnostics)
    {
        if (!string.Equals(MatchAttributeValue(aumlText, "CreateInDesignTime"), "true", StringComparison.OrdinalIgnoreCase))
            return null;

        var viewModel = MatchAttributeValue(aumlText, "ViewModel");
        if (viewModel == null) return null;
        viewModel = UnwrapTypeExtension(viewModel);   // accept both "prefix:Type" and "{x:Type prefix:Type}"

        var type = ResolveMarkupType(aumlText, viewModel);
        if (type == null)
        {
            diagnostics.Add($"x:ViewModel '{viewModel}' was not found in the loaded assemblies");
            return null;
        }

        try
        {
            return _app.UIContext.Resolve(type);
        }
        catch (Exception e)
        {
            diagnostics.Add($"x:ViewModel {type.Name} could not be built: {e.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>Unwraps the x:Type markup extension: <c>{x:Type prefix:Type}</c> -> <c>prefix:Type</c>. A plain
    /// <c>prefix:Type</c> is returned unchanged. (The canonical form is positional with a space, not <c>=</c>.)</summary>
    private static string UnwrapTypeExtension(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("{") || !value.EndsWith("}")) return value;
        var inner = value[1..^1].Trim();                 // e.g. "x:Type prefix:Type"
        var space = inner.IndexOf(' ');
        return space < 0 ? inner : inner[(space + 1)..].Trim();
    }

    /// <summary>Value of the first <c>[prefix:]localName="..."</c> attribute in the text (prefix optional).</summary>
    private static string MatchAttributeValue(string text, string localName)
    {
        var m = Regex.Match(text, $@"(?:\w+:)?{Regex.Escape(localName)}\s*=\s*""([^""]*)""");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>Resolves a markup type reference (<c>prefix:Type</c>) to a CLR type using the buffer's xmlns
    /// declarations (clr-namespace) against the assemblies loaded in this host (engine + the project assembly).</summary>
    private static Type ResolveMarkupType(string aumlText, string qualified)
    {
        var prefix = "";
        var local = qualified;
        var colon = qualified.IndexOf(':');
        if (colon >= 0) { prefix = qualified[..colon]; local = qualified[(colon + 1)..]; }

        var xmlnsPattern = prefix.Length == 0 ? @"xmlns\s*=\s*""([^""]*)""" : $@"xmlns:{Regex.Escape(prefix)}\s*=\s*""([^""]*)""";
        var xm = Regex.Match(aumlText, xmlnsPattern);
        string clrNamespace = null, assemblyName = null;
        if (xm.Success && xm.Groups[1].Value.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var rest = xm.Groups[1].Value["clr-namespace:".Length..];
            var sc = rest.IndexOf(';');
            clrNamespace = (sc < 0 ? rest : rest[..sc]).Trim();
            var ai = rest.IndexOf("assembly=", StringComparison.Ordinal);
            if (ai >= 0) assemblyName = rest[(ai + "assembly=".Length)..].Trim();
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assemblyName != null && !string.Equals(asm.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)) continue;
            var type = clrNamespace != null
                ? asm.GetType(clrNamespace + "." + local, throwOnError: false)
                : SafeGetTypes(asm).FirstOrDefault(t => t.Name == local);
            if (type != null) return type;
        }
        return null;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm) => Adamantium.Core.Reflection.LoadableTypes.Of(asm);

    /// <summary>Design size for a dimension: the declared value if set, else the request, else the default.</summary>
    private static double ResolveDimension(double? declared, uint? request, double fallback)
    {
        if (declared is > 0 && !double.IsNaN(declared.Value)) return declared.Value;
        if (request is > 0) return request.Value;
        return fallback;
    }

    // Wall-clock budget for the design-time universe snapshot: enough for the universe to parse+load its model content
    // (async) and publish a stable frame. A one-shot preview, so a few seconds is acceptable.
    private const double UniverseSnapshotSeconds = 15.0;

    /// <summary>
    /// Drives every universe hosted in the markup (a design-aware behavior created it bound to a RenderTargetPanel) for
    /// a short snapshot: each iteration runs one frame of it and waits the device idle, with a tiny sleep so its
    /// async content load completes. RunOnce publishes the frame to the panel (CopyOutput), so the panel samples it
    /// when the content render path composites below. No-op when the markup hosts no universe.
    /// </summary>
    private void DriveDesignTimeUniverses()
    {
        var universes = _universeService?.Universes;
        if (universes is not { Count: > 0 }) return;

        var total = TimeSpan.Zero;
        const double dt = 1.0 / 60.0;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(UniverseSnapshotSeconds);
        ulong frame = 0;
        while (DateTime.UtcNow < deadline)
        {
            total += TimeSpan.FromSeconds(dt);
            var time = new AppTime { FramesCount = ++frame, FrameTime = dt, TotalTime = total };
            foreach (var universe in universes)
            {
                universe.RunOnce(time);
                universe.Submit();
            }
            _device.DeviceWaitIdle();
            // The app's EndScene does this; without it the universes' per-frame buffer pools never reset and fill the BAR.
            _deviceService.RaiseFrameFinished();
            Thread.Sleep(8);   // let the async content-load tasks make progress between frames
        }
        _device.DeviceWaitIdle();
    }

    /// <summary>
    /// Maps a point in the rendered frame's design space to the authored element under it (designer go-to-source /
    /// hover frame): hit-tests the last rendered tree, walks up to the nearest element that came from the AUML
    /// (template-internal parts have no source position), and returns its markup line/column plus its rect in design
    /// space. Null when nothing is rendered or nothing authored sits under the point.
    /// </summary>
    public HitTestResult HitTest(double x, double y)
    {
        if (FindAuthoredAt(x, y) is not { } found) return null;
        var (current, file, line, column) = found;

        // WorldTransform is already accumulated up the tree → its translation is the element's origin in design space;
        // most controls' rect is origin + RenderSize. A shape's layout box, though, spans from (0,0) to the geometry's
        // max (it reserves space for the geometry's coordinate origin, like WPF Stretch=None), so for a Path/Line we
        // report the geometry's tight bounds instead.
        var pos = current.WorldTransform.TranslationVector;
        if (current is Adamantium.UI.Controls.Shapes.Path { Data: { } geometry })
        {
            geometry.RecalculateBounds();
            var b = geometry.Bounds;
            return new HitTestResult(line, column, pos.X + b.X, pos.Y + b.Y, b.Width, b.Height, file);
        }
        if (current is Adamantium.UI.Controls.Shapes.Line ln)
        {
            double minX = Math.Min(ln.X1, ln.X2), minY = Math.Min(ln.Y1, ln.Y2);
            return new HitTestResult(line, column,
                pos.X + minX, pos.Y + minY, Math.Abs(ln.X2 - ln.X1), Math.Abs(ln.Y2 - ln.Y1), file);
        }
        var size = current.RenderSize;
        return new HitTestResult(line, column, pos.X, pos.Y, size.Width, size.Height, file);
    }

    // Hit-tests the last rendered tree at (x,y) in design space and walks up to the nearest element with a place in
    // markup: one of the previewed file's (File null), or one a nested view's generated code recorded (Design.Source) -
    // template-internal parts have neither. Shared by HitTest, SelectAt and Hover.
    private (IUIComponent Component, string File, int Line, int Column)? FindAuthoredAt(double x, double y)
    {
        if (_lastWindow is not IUIComponent root || _lastSourceMap is null) return null;

        // GetVisualsAt (not HitTest): the designer must select ANY authored element under the cursor, including a
        // non-input visual like a Border or a Shape. HitTest returns only IInputComponent targets, so clicking a bare
        // Border fell through to the nearest interactive element behind it (e.g. the containing panel) - the "selects the
        // background panel, not the control in front" bug.
        var hit = root.GetVisualsAt(new Vector2(x, y)).FirstOrDefault();
        for (var current = hit; current is not null; current = current.VisualParent)
        {
            if (_lastSourceMap.TryGetValue(current, out var span))
            {
                return (current, null, span.Line, span.Position);
            }

            if (Design.SourceOf(current) is { } source)
            {
                return (current, source.File, source.Line, source.Column);
            }
        }

        return null;
    }

    /// <summary>Selects the authored element at a design-space point (the framework draws the frame) and returns the new
    /// frame plus its markup position; a miss clears the selection.</summary>
    public RenderResult SelectAt(double x, double y, string outPath)
    {
        if (_lastWindow == null || _renderService == null || _liveWindow == null)
            return RenderResult.Fail("no live session to select in", null);

        if (ShowTabAt(x, y))
        {
            _lastWindow.Update(_app.ThemeManager, new AppTime());
        }

        var found = FindAuthoredAt(x, y);
        SetWindowSelection(found?.Component as UIComponent);   // null clears the selection

        if (!_renderService.RenderHeadlessFrame(_liveWindow, _liveScale, new AppTime()))
            return RenderResult.Fail("render failed", null);
        _renderService.SaveFrameRaw(outPath);

        var hit = found is { } f ? new HitTestResult(f.Line, f.Column, 0, 0, 0, 0, f.File) : null;
        // Report the real animation state so the editor keeps streaming after a click - the selection frame persists on
        // the AdornerLayer, so the resumed stream's frames carry it; the preview doesn't freeze on selecting.
        var animating = AnimationManager.HasActiveAnimations || DesignTimeMediaClock.HasActiveMedia;
        return RenderResult.Ok([outPath], null, _liveTargetWidth, _liveTargetHeight, _liveScale,
            _liveDesignWidth, _liveDesignHeight, animating: animating, hit: hit);
    }

    // Drives the previewed window's framework AdornerLayer via the IAdornerHost contract (works for the VirtualWindow
    // the designer hosts in AND a real WindowBase). Passing null clears the selection; the adorner processor reads
    // window.Adorners on the next render and draws the frame on top.
    private void SetWindowSelection(UIComponent element)
    {
        if (_lastWindow is not IAdornerHost host) return;
        if (element is null) host.AdornerLayer.SetSelection(null);
        else host.AdornerLayer.SetSelection([element]);
    }

    /// <summary>
    /// Designer hover: like <see cref="SelectAt"/> but for the transient hover frame - drives the previewed window's
    /// AdornerLayer hover (so the FRAMEWORK draws the stroke-aware hover frame, not a host-side rect), re-renders one
    /// frame and returns it. A null point, a miss, or hovering an already-selected element clears the hover frame.
    /// </summary>
    public RenderResult Hover(double? x, double? y, string outPath)
    {
        if (_lastWindow == null || _renderService == null || _liveWindow == null)
            return RenderResult.Fail("no live session to hover in", null);

        var element = x.HasValue && y.HasValue ? FindAuthoredAt(x.Value, y.Value)?.Component as UIComponent : null;

        // Moving the cursor WITHIN the same element changes nothing visible, so skip the (whole-scene) re-render and
        // return no frame - the editor keeps its current one. Only an element change actually re-renders. This keeps a
        // mouse sweep / a wheel-zoom burst from flooding the GPU with full-size renders.
        if (ReferenceEquals(element, _lastHoverElement))
            return RenderResult.Ok([], null, _liveTargetWidth, _liveTargetHeight, _liveScale, _liveDesignWidth, _liveDesignHeight);
        _lastHoverElement = element;

        SetWindowHover(element);   // null/miss clears the hover frame

        if (!_renderService.RenderHeadlessFrame(_liveWindow, _liveScale, new AppTime()))
            return RenderResult.Fail("render failed", null);
        _renderService.SaveFrameRaw(outPath);

        var animating = AnimationManager.HasActiveAnimations || DesignTimeMediaClock.HasActiveMedia;
        return RenderResult.Ok([outPath], null, _liveTargetWidth, _liveTargetHeight, _liveScale,
            _liveDesignWidth, _liveDesignHeight, animating: animating);
    }

    // Drives the previewed window's framework AdornerLayer hover via IAdornerHost. Null clears it.
    private void SetWindowHover(UIComponent element)
    {
        if (_lastWindow is IAdornerHost host) host.AdornerLayer.SetHover(element);
    }

    private DesignerRenderService GetRenderService(IWindow window)
    {
        // One service for the whole session: created lazily with the SHARED device, then re-pointed/resized per window
        // (RenderHeadlessFrame). Recreating it per render would churn the device/presenter and leak the render cache.
        if (_renderService == null)
        {
            _renderService = new DesignerRenderService(_app.EntityWorld, window, _device);
            _renderService.GraphicsDevice.ClearColor = Colors.White;   // designer canvas (matches the old OffscreenRenderer)
        }
        return _renderService;
    }

    public void Dispose() => _renderService?.UnloadContent();
}
