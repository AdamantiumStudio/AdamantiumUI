using System.Runtime.InteropServices.Marshalling;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.Windows.Automation;

[GeneratedComClass]
internal sealed partial class UiaWindowProvider : UiaProvider, IRawElementProviderFragmentRoot
{
    public UiaWindowProvider(UiaBridge bridge) : base(bridge)
    {
    }

    public override AutomationPeer FindPeer() => Bridge.WindowPeer();

    public override IRawElementProviderSimple GetHostRawElementProvider() =>
        UiaInterop.UiaHostProviderFromHwnd(Bridge.Handle, out var host) == 0 ? host : null;

    public override nint GetRuntimeId() => 0;

    public override UiaRect GetBoundingRectangle() => default;

    public IRawElementProviderFragment ElementProviderFromPoint(double x, double y) =>
        Bridge.Run(() => Bridge.ProviderAt(new PixelPoint(x, y)));

    public IRawElementProviderFragment GetFocus() => Bridge.Run(() =>
    {
        var focused = FocusManager.Focused as IUIComponent;
        if (focused == null || !ReferenceEquals(focused.RootVisual, Bridge.Window))
        {
            return null;
        }

        var provider = Bridge.ProviderOf(focused);
        return ReferenceEquals(provider, this) ? null : provider;
    });
}
