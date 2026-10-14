using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Adamantium.Navigation;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Navigation;

/// <summary>Wires a region to a host control: resolves the adapter for the control's type from the app-registered
/// <see cref="RegionAdapterMappings"/> and attaches. Used by the <c>Region.Source</c> and <c>RegionManager.RegionName</c>
/// attached properties.</summary>
internal static class RegionHost
{
    // Which region each host shows, and which hosts show each region - so a closing window can tell the regions only it
    // showed. Both weak: a region outlives its hosts, and the manager outlives both.
    private static readonly ConditionalWeakTable<IUIComponent, IRegion> RegionOf = new();
    private static readonly ConditionalWeakTable<IRegion, List<WeakReference<IUIComponent>>> HostsOf = new();

    /// <param name="named">Declared by name (<c>RegionManager.RegionName</c>): the window it is declared in owns it. A
    /// region a view model hands over (<c>Region.Source</c>) is the view model's, and outlives any window showing it.</param>
    public static void Attach(IRegion region, IUIComponent host, bool named)
    {
        if (region == null || host == null) return;
        var mappings = UIAppContext.Current?.UIContext?.Resolve<RegionAdapterMappings>();
        mappings?.GetAdapter(host.GetType())?.Attach(region, host);
        if (named) Track(region, host);
    }

    internal static void Track(IRegion region, IUIComponent host)
    {
        RegionOf.AddOrUpdate(host, region);
        var hosts = HostsOf.GetValue(region, static _ => []);
        hosts.RemoveAll(static weak => !weak.TryGetTarget(out _));
        hosts.Add(new WeakReference<IUIComponent>(host));
    }

    /// <summary>The window is closing: every region shown in it and by nothing outside it goes from
    /// <paramref name="manager"/>, letting go of its view models - the next window asking for the name gets a fresh one.
    /// A region a host outside the window still shows (a docked pane torn off into another window) stays.</summary>
    internal static void ReleaseRegionsIn(IUIComponent window, IRegionManager manager)
    {
        if (manager == null) return;

        var regions = new List<IRegion>();
        Collect(window, regions);
        foreach (var region in regions)
        {
            if (ShownOutside(region, window)) continue;
            Observe(manager.RemoveRegionAsync(region));
        }
    }

    // Nobody waits on a closed window's regions, so a view model that fails on the way out is reported here.
    private static async void Observe(Task leaving)
    {
        try
        {
            await leaving;
        }
        catch (Exception ex)
        {
            Serilog.Log.Logger.Error($"A view model failed leaving a closed window's region: {ex}");
        }
    }

    private static void Collect(IUIComponent node, List<IRegion> regions)
    {
        if (RegionOf.TryGetValue(node, out var region) && !regions.Contains(region)) regions.Add(region);
        foreach (var child in node.VisualChildren) Collect(child, regions);
    }

    private static bool ShownOutside(IRegion region, IUIComponent window)
    {
        if (!HostsOf.TryGetValue(region, out var hosts)) return false;

        foreach (var weak in hosts)
        {
            if (!weak.TryGetTarget(out var host)) continue;
            if (host is FundamentalUIComponent { IsDiscarded: true }) continue;
            if (!ReferenceEquals(RootOf(host), window)) return true;
        }

        return false;
    }

    private static IUIComponent RootOf(IUIComponent node)
    {
        while (node.VisualParent is { } parent) node = parent;
        return node;
    }
}
