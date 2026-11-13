using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;

namespace Adamantium.XamlTests;

/// <summary>A page kept between visits (<c>x:KeepAlive</c> Required).</summary>
internal sealed class KeptPage : Border
{
    public override NavigationCacheMode KeepAlive => NavigationCacheMode.Required;
}
