using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Docking;

/// <summary>What to do with panes that hold unsaved work before they close.</summary>
public enum UnsavedAnswer
{
    /// <summary>Not answered yet: the area asks.</summary>
    None,

    Save,

    /// <summary>Close them and lose the changes.</summary>
    Discard,

    /// <summary>Close nothing.</summary>
    Cancel
}

/// <summary>The one question a docking area asks before closing panes with unsaved work, in its own overlay window. The
/// area says how many and which is the first; the theme says it in words and gives the answers PART_Save,
/// PART_Discard and PART_Cancel.</summary>
public class UnsavedQuestion : Control
{
    /// <summary>How many panes the answer is about.</summary>
    public static readonly AdamantiumProperty CountProperty = AdamantiumProperty.Register(nameof(Count),
        typeof(int), typeof(UnsavedQuestion), new PropertyMetadata(0));

    /// <summary>The header of the first of them - the whole of it when it is one.</summary>
    public static readonly AdamantiumProperty SubjectProperty = AdamantiumProperty.Register(nameof(Subject),
        typeof(object), typeof(UnsavedQuestion), new PropertyMetadata(null));

    private ButtonBase _save;
    private ButtonBase _discard;
    private ButtonBase _cancel;

    public int Count
    {
        get => GetValue<int>(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public object Subject
    {
        get => GetValue(SubjectProperty);
        set => SetValue(SubjectProperty, value);
    }

    public event Action<UnsavedAnswer> Answered;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _save = GetTemplateChild("PART_Save") as ButtonBase;
        _discard = GetTemplateChild("PART_Discard") as ButtonBase;
        _cancel = GetTemplateChild("PART_Cancel") as ButtonBase;

        if (_save != null) _save.Click += OnSave;
        if (_discard != null) _discard.Click += OnDiscard;
        if (_cancel != null) _cancel.Click += OnCancel;
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();

        if (_save != null) _save.Click -= OnSave;
        if (_discard != null) _discard.Click -= OnDiscard;
        if (_cancel != null) _cancel.Click -= OnCancel;

        _save = null;
        _discard = null;
        _cancel = null;
    }

    private void OnSave(object sender, RoutedEventArgs e) => Answered?.Invoke(UnsavedAnswer.Save);

    private void OnDiscard(object sender, RoutedEventArgs e) => Answered?.Invoke(UnsavedAnswer.Discard);

    private void OnCancel(object sender, RoutedEventArgs e) => Answered?.Invoke(UnsavedAnswer.Cancel);

    protected override AutomationPeer OnCreateAutomationPeer() => new QuestionAutomationPeer(this);
}
