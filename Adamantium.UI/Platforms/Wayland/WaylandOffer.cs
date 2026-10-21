using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using Adamantium.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>A data offer and what it says about itself.</summary>
internal sealed class WaylandOffer
{
    private readonly List<string> types = new();
    private readonly object _typesLock = new();

    public WaylandOffer(WlDataOffer proxy)
    {
        Proxy = proxy;
        proxy.Offer += type =>
        {
            lock (_typesLock)
            {
                types.Add(type);
            }
        };
        proxy.SourceActions += actions => SourceActions = actions;
        proxy.Action += action => Action = action;
    }

    public WlDataOffer Proxy { get; }

    public WlDataDeviceManager.DndAction SourceActions { get; private set; }

    public WlDataDeviceManager.DndAction Action { get; private set; }

    public bool Has(string type)
    {
        lock (_typesLock)
        {
            return types.Contains(type);
        }
    }

    public string FirstOf(string[] wanted)
    {
        lock (_typesLock)
        {
            return wanted.FirstOrDefault(types.Contains);
        }
    }

    public string First()
    {
        lock (_typesLock)
        {
            return types.FirstOrDefault();
        }
    }

    public void Destroy()
    {
        if (Proxy.IsAlive)
        {
            Proxy.Destroy();
        }
    }
}
