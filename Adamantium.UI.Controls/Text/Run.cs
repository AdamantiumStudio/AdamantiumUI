using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>
/// A run of text inside a <see cref="TextBlock"/>, with its own optional formatting (see <see cref="Inline"/>); the runs
/// of a TextBlock lay out as one text and wrap across each other. Every property is a bindable
/// <see cref="AdamantiumProperty"/> and the run inherits the TextBlock's DataContext, so <c>Text</c> / <c>Foreground</c>
/// bind straight to the view-model (<c>&lt;Run Text="{Binding Name}" Foreground="{Binding Color}"/&gt;</c>).
/// </summary>
public class Run : Inline
{
    public static readonly AdamantiumProperty TextProperty = AdamantiumProperty.Register(nameof(Text),
        typeof(string), typeof(Run), new PropertyMetadata(string.Empty, OnTextChanged));

    public string Text
    {
        get => GetValue<string>(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
        => (a as Run)?.RaiseChanged();
}
