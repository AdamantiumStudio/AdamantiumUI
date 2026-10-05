using System;
using System.Runtime.InteropServices.Marshalling;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComClass]
internal sealed partial class UiaElementProvider : UiaProvider
{
    private readonly WeakReference<AutomationPeer> _peer;

    public UiaElementProvider(UiaBridge bridge, AutomationPeer peer) : base(bridge)
    {
        _peer = new WeakReference<AutomationPeer>(peer);
    }

    public override AutomationPeer FindPeer() => _peer.TryGetTarget(out var peer) ? peer : null;

    public override IRawElementProviderSimple GetHostRawElementProvider() => null;

    public override nint GetRuntimeId() => Bridge.Run(() => UiaSafeArray.Of(UiaInterop.AppendRuntimeId, Peer().RuntimeId));

    public override UiaRect GetBoundingRectangle() => Bridge.Run(() => ToRect(Peer().BoundingRectangle));
}
