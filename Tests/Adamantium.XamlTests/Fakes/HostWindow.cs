using Adamantium.UI.Controls;
using Adamantium.UI.Core;

namespace Adamantium.XamlTests;

/// <summary>A window as the application shows one, with no screen behind it: given its context, it is themed by it and
/// hears what the application tells its windows - a new language - until it is closed.</summary>
internal sealed class HostWindow : Window
{
    protected override IWindowWorkerService CreateWindowWorker(IUIContext context) => null;

    public static HostWindow Around(object content, double width, double height, IUIContext context)
    {
        var window = new HostWindow
        {
            Width = width, Height = height, ClientWidth = width, ClientHeight = height, Content = content
        };
        window.AttachContextAndInitialize(context);
        return window;
    }
}
