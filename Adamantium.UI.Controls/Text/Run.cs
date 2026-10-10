using Adamantium.UI.Core;

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
        typeof(string), typeof(Run), new PropertyMetadata(string.Empty, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>The run's text; in markup it can be written as the element's content: <c>&lt;Run&gt;text&lt;/Run&gt;</c>.</summary>
    [Content]
    public string Text
    {
        get => GetValue<string>(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
