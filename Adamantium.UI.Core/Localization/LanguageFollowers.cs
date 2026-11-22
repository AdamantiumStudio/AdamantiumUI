using System.Linq;
using System.Runtime.CompilerServices;
using Adamantium.Localization;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Core.Localization;

/// <summary>The bindings that write again when the application's language changes - a {Localize}, a value written in the
/// language's culture. Held weakly: a binding dropped without closing is not kept alive by the language.</summary>
internal static class LanguageFollowers
{
    private static readonly ConditionalWeakTable<BindingExpressionBase, object> Followers = new();

    // As the assembly loads, before any of the UI subscribes: the bindings are told first, and what follows the language
    // otherwise - a window, a docked pane's title - finds them written in it.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void FollowTheLanguage() => Languages.Changed += OnLanguageChanged;
#pragma warning restore CA2255

    public static void Follow(BindingExpressionBase binding) => Followers.AddOrUpdate(binding, null);

    public static void Unfollow(BindingExpressionBase binding) => Followers.Remove(binding);

    private static void OnLanguageChanged(object sender, EventArgs e)
    {
        foreach (var binding in Followers.Select(follower => follower.Key).ToList())
        {
            binding.OnLanguageChanged();
        }
    }
}
