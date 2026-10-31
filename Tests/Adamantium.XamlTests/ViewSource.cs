using Adamantium.UI.Core;

namespace Adamantium.XamlTests;

/// <summary>A view model handing out one view, for a binding to show it.</summary>
public sealed class ViewSource(IUIComponent view)
{
    public IUIComponent View { get; } = view;
}
