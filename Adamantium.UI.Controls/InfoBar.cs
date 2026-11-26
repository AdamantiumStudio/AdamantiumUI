using System;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>How much an <see cref="InfoBar"/>'s message matters. The theme gives each its own colour and icon, so it is
/// told apart without the colour too.</summary>
public enum InfoBarSeverity
{
    /// <summary>Something worth knowing: a file not made yet.</summary>
    Informational,

    /// <summary>Something done: a layer renamed.</summary>
    Success,

    /// <summary>Something to keep in mind: a change that waits for the next run.</summary>
    Warning,

    /// <summary>Something that went wrong: a file that cannot be read.</summary>
    Error
}

/// <summary>A message across a page: what happened (<see cref="Title"/>), why (<see cref="Message"/>), and what can be
/// done - the actions it holds as its <see cref="ContentControl.Content"/>, under the text. Closed by its close button
/// while <see cref="IsClosable"/>, or by <see cref="IsOpen"/>; closed, it takes no room. Templated: PART_CloseButton
/// closes it.</summary>
public class InfoBar : ContentControl
{
    public static readonly AdamantiumProperty SeverityProperty = AdamantiumProperty.Register(nameof(Severity),
        typeof(InfoBarSeverity), typeof(InfoBar), new PropertyMetadata(InfoBarSeverity.Informational));

    // An empty text is no text: the template has no line to give it.
    public static readonly AdamantiumProperty TitleProperty = AdamantiumProperty.Register(nameof(Title),
        typeof(string), typeof(InfoBar),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, NothingForEmpty));

    public static readonly AdamantiumProperty MessageProperty = AdamantiumProperty.Register(nameof(Message),
        typeof(string), typeof(InfoBar),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, NothingForEmpty));

    public static readonly AdamantiumProperty IsOpenProperty = AdamantiumProperty.Register(nameof(IsOpen),
        typeof(bool), typeof(InfoBar),
        new PropertyMetadata(true, PropertyMetadataOptions.BindsTwoWayByDefault | PropertyMetadataOptions.AffectsMeasure,
            (bar, _) => bar.CoerceValue(VisibilityProperty)));

    public static readonly AdamantiumProperty IsClosableProperty = AdamantiumProperty.Register(nameof(IsClosable),
        typeof(bool), typeof(InfoBar), new PropertyMetadata(true));

    private ButtonBase _closeButton;

    // Closed, it takes no room whatever its own Visibility or its theme says.
    static InfoBar()
    {
        VisibilityProperty.OverrideMetadata(typeof(InfoBar), new PropertyMetadata(Visibility.Visible, null,
            static (bar, visibility) => ((InfoBar)bar).IsOpen ? visibility : Visibility.Collapsed));
    }

    private static object NothingForEmpty(AdamantiumComponent bar, object text) =>
        text is string { Length: 0 } ? null : text;

    public InfoBarSeverity Severity
    {
        get => GetValue<InfoBarSeverity>(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    /// <summary>What happened, in a few words.</summary>
    public string Title
    {
        get => GetValue<string>(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Why, and what it means for the user.</summary>
    public string Message
    {
        get => GetValue<string>(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Whether it shows. Two-way bindable: closed by its button, it says so to whoever opened it.</summary>
    public bool IsOpen
    {
        get => GetValue<bool>(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>Whether it shows a close button; a message that must stay until its cause is gone does not.</summary>
    public bool IsClosable
    {
        get => GetValue<bool>(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    /// <summary>Raised when it is closed - by its close button or <see cref="Close"/>; not when whoever shows it sets
    /// <see cref="IsOpen"/>, who knows already.</summary>
    public event EventHandler Closed;

    /// <summary>Closes it, as its close button does.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        SetCurrentValue(IsOpenProperty, false);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_closeButton != null) _closeButton.Click -= OnCloseClicked;
        _closeButton = GetTemplateChild("PART_CloseButton") as ButtonBase;
        if (_closeButton != null) _closeButton.Click += OnCloseClicked;
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        if (_closeButton != null) _closeButton.Click -= OnCloseClicked;
        _closeButton = null;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new InfoBarAutomationPeer(this);

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
