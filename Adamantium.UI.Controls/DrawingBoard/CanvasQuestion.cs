using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>What a canvas asks about before it takes something away.</summary>
public enum CanvasQuestionKind
{
    /// <summary>Whether to delete what is selected.</summary>
    DeleteSelection,

    /// <summary>Whether to empty the whole plane.</summary>
    Clear
}

/// <summary>The question a canvas asks before it takes something away, in its own overlay window. The canvas says what
/// it is about and how many things it touches; the theme says it in words and gives the two answers, PART_Yes and
/// PART_No.</summary>
public class CanvasQuestion : Control
{
    public static readonly AdamantiumProperty KindProperty = AdamantiumProperty.Register(nameof(Kind),
        typeof(CanvasQuestionKind), typeof(CanvasQuestion), new PropertyMetadata(CanvasQuestionKind.DeleteSelection));

    /// <summary>How many things the answer touches.</summary>
    public static readonly AdamantiumProperty CountProperty = AdamantiumProperty.Register(nameof(Count),
        typeof(int), typeof(CanvasQuestion), new PropertyMetadata(0));

    private ButtonBase _yes;
    private ButtonBase _no;

    public CanvasQuestionKind Kind
    {
        get => GetValue<CanvasQuestionKind>(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public int Count
    {
        get => GetValue<int>(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>Raised with the answer: true for yes.</summary>
    public event Action<bool> Answered;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _yes = GetTemplateChild("PART_Yes") as ButtonBase;
        _no = GetTemplateChild("PART_No") as ButtonBase;

        if (_yes != null) _yes.Click += OnYes;
        if (_no != null) _no.Click += OnNo;
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();

        if (_yes != null) _yes.Click -= OnYes;
        if (_no != null) _no.Click -= OnNo;

        _yes = null;
        _no = null;
    }

    private void OnYes(object sender, RoutedEventArgs e) => Answered?.Invoke(true);

    private void OnNo(object sender, RoutedEventArgs e) => Answered?.Invoke(false);

    protected override AutomationPeer OnCreateAutomationPeer() => new QuestionAutomationPeer(this);
}
