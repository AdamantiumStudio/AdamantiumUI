using System.Linq;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Text;

/// <summary>Spelling checked in a text box, as WPF's <c>SpellCheck</c>: a misspelled word gets a wavy line, and the
/// box's menu offers what it may have been meant as. Set on a box or on any element above it; off by default. The words
/// are the system's, in the box's <c>Language</c>, else the application's language, else English.</summary>
public static class SpellCheck
{
    public static readonly AdamantiumProperty IsEnabledProperty = AdamantiumProperty.RegisterAttached("IsEnabled",
        typeof(bool), typeof(AdamantiumComponent),
        new PropertyMetadata(false, PropertyMetadataOptions.Inherits, OnIsEnabledChanged));

    public static bool GetIsEnabled(AdamantiumComponent element) => element.GetValue<bool>(IsEnabledProperty);

    public static void SetIsEnabled(AdamantiumComponent element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not TextBox box)
        {
            return;
        }

        var attached = box.Behaviors.OfType<SpellCheckBehavior>().FirstOrDefault();
        if (e.NewValue is true && attached == null)
        {
            box.Behaviors.Add(new SpellCheckBehavior());
        }
        else if (e.NewValue is not true && attached != null)
        {
            box.Behaviors.Remove(attached);
        }
    }
}
