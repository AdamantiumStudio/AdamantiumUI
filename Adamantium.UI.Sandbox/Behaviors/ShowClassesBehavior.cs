using System.Collections.Specialized;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Sandbox.Behaviors;

/// <summary>
/// View-layer behavior: writes the classes an element carries right now - the ones selectors match - into its text
/// block, and keeps them current as a style or a trigger adds and takes back its own.
/// </summary>
public class ShowClassesBehavior : Behavior<TextBlock>
{
    /// <summary>The element whose classes are shown, bound as <c>Of="{Binding ElementName=Name}"</c>.</summary>
    public static readonly AdamantiumProperty OfProperty = AdamantiumProperty.Register(nameof(Of),
        typeof(IFundamentalUIComponent), typeof(ShowClassesBehavior), new PropertyMetadata(null, OnOfChanged));

    private TextBlock _text;
    private IFundamentalUIComponent _watched;

    public IFundamentalUIComponent Of
    {
        get => GetValue(OfProperty) as IFundamentalUIComponent;
        set => SetValue(OfProperty, value);
    }

    private static void OnOfChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var behavior = (ShowClassesBehavior)component;
        if (behavior._text != null) behavior.Watch(e.NewValue as IFundamentalUIComponent);
    }

    protected override void OnAttached(TextBlock text)
    {
        _text = text;
        Watch(Of);
    }

    protected override void OnDetached(TextBlock text)
    {
        Watch(null);
        _text = null;
    }

    private void Watch(IFundamentalUIComponent element)
    {
        if (ReferenceEquals(element, _watched)) return;

        if (_watched != null) _watched.ClassNames.CollectionChanged -= OnClassesChanged;
        _watched = element;
        if (_watched != null) _watched.ClassNames.CollectionChanged += OnClassesChanged;

        Show();
    }

    private void OnClassesChanged(object sender, NotifyCollectionChangedEventArgs e) => Show();

    private void Show()
    {
        if (_text != null) _text.Text = _watched?.ClassNames.ToString();
    }
}
