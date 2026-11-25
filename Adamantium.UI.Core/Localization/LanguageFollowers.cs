using System.Linq;
using System.Runtime.CompilerServices;
using Adamantium.Localization;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Core.Localization;

/// <summary>The bindings that write again when the application's language changes - a {Localize}, a value written in the
/// language's culture. Held weakly: a binding dropped without closing is not kept alive by the language.</summary>
internal static class LanguageFollowers
{
    // The value says whether the binding is still in use: the table itself gives out its keys until their finalization is
    // over, when what they hold may be finalized already.
    private static readonly ConditionalWeakTable<BindingExpressionBase, WeakReference<BindingExpressionBase>> Followers = new();

    // As the assembly loads, before any of the UI subscribes: the bindings are told first, and what follows the language
    // otherwise - a window, a docked pane's title - finds them written in it.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void FollowTheLanguage() => Languages.Changed += OnLanguageChanged;
#pragma warning restore CA2255

    public static void Follow(BindingExpressionBase binding) =>
        Followers.GetValue(binding, static follower => new WeakReference<BindingExpressionBase>(follower));

    public static void Unfollow(BindingExpressionBase binding) => Followers.Remove(binding);

    private static void OnLanguageChanged(object sender, EventArgs e)
    {
        foreach (var follower in Followers.Select(follower => follower.Value).ToList())
        {
            if (follower.TryGetTarget(out var binding))
            {
                binding.OnLanguageChanged();
            }
        }
    }
}
