using Adamantium.Navigation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Controls;

namespace Adamantium.UI.Controls.Docking;

/// <summary>The unit of docking: a <see cref="TabItem"/> with header, content, and the policy of where it may live, set as
/// ordinary properties. A file whose root is <c>&lt;Pane&gt;</c> is a class of its own, the way a <c>&lt;View&gt;</c> is.</summary>
public class Pane : TabItem, IView
{
    public Pane()
    {
        InitializeComponent();
    }

    /// <summary>Builds the pane's content. Overridden by the class generated from a <c>&lt;Pane&gt;</c> file.</summary>
    protected virtual void InitializeComponent()
    {
    }

    /// <summary>Where this pane sits. STATE, not a command - it is two-way bindable, so a drag writes it and a
    /// view-model can read it, or set it (<c>Placement = DockZone.Floating</c>) to send the pane away. One setter for
    /// both directions means the gesture and the code cannot drift apart.</summary>
    public static readonly AdamantiumProperty ZoneProperty = AdamantiumProperty.Register(nameof(Zone),
        typeof(DockZone), typeof(Pane), new PropertyMetadata(DockZone.Center));

    /// <summary>Where this pane MAY go. The whole vocabulary of restrictions is data, so it serializes and can be read
    /// at a glance; the rare "not here, but only on Tuesdays" case is served by the cancellable docking event instead
    /// of a predicate nobody can see.</summary>
    public static readonly AdamantiumProperty AllowedProperty = AdamantiumProperty.Register(nameof(Allowed),
        typeof(DockZone), typeof(Pane), new PropertyMetadata(DockZone.All));

    /// <summary>Smallest useful size along the docked axis; a split that would go below it is refused. Zero by default, where
    /// the group's tab strip is the only floor.</summary>
    public static readonly AdamantiumProperty MinSizeProperty = AdamantiumProperty.Register(nameof(MinSize),
        typeof(double), typeof(Pane), new PropertyMetadata(0.0));

    /// <summary>Document (default) or tool, <see cref="PaneKind"/>: what closing means and whether a saved layout restores it.
    /// The look comes from where the group stands.</summary>
    public static readonly AdamantiumProperty KindProperty = AdamantiumProperty.Register(nameof(Kind),
        typeof(PaneKind), typeof(Pane), new PropertyMetadata(PaneKind.Document));

    public PaneKind Kind
    {
        get => GetValue<PaneKind>(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Holds work not saved yet: the tab shows it, and closing asks first (see
    /// <see cref="DockingArea.AsksBeforeClosingUnsaved"/>). A docking region keeps it from the view model's
    /// <see cref="IDocument.IsDirty"/>.</summary>
    public static readonly AdamantiumProperty IsDirtyProperty = AdamantiumProperty.Register(nameof(IsDirty),
        typeof(bool), typeof(Pane), new PropertyMetadata(false));

    public bool IsDirty
    {
        get => GetValue<bool>(IsDirtyProperty);
        set => SetValue(IsDirtyProperty, value);
    }

    /// <summary>Which way this pane's tab is turned, set by the group when it folds against a side edge. Three states so the
    /// theme can pick a template; AffectsParentMeasure, since turning changes what the strip must hold.</summary>
    public static readonly AdamantiumProperty LabelRotationProperty = AdamantiumProperty.Register(nameof(LabelRotation),
        typeof(PaneLabelRotation), typeof(Pane),
        new PropertyMetadata(PaneLabelRotation.None,
            PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsParentMeasure));

    public PaneLabelRotation LabelRotation
    {
        get => GetValue<PaneLabelRotation>(LabelRotationProperty);
        set => SetValue(LabelRotationProperty, value);
    }

    /// <summary>Whether this pane comes back with a restored layout. True for tools (part of the workspace); false for
    /// documents and for throwaway utilities, whose existence belongs to a session, not to the arrangement.</summary>
    public static readonly AdamantiumProperty RestoreProperty = AdamantiumProperty.Register(nameof(Restore),
        typeof(bool), typeof(Pane), new PropertyMetadata(true));

    public DockZone Zone
    {
        get => GetValue<DockZone>(ZoneProperty);
        set => SetValue(ZoneProperty, value);
    }

    public DockZone Allowed
    {
        get => GetValue<DockZone>(AllowedProperty);
        set => SetValue(AllowedProperty, value);
    }

    public double MinSize
    {
        get => GetValue<double>(MinSizeProperty);
        set => SetValue(MinSizeProperty, value);
    }

    public bool Restore
    {
        get => GetValue<bool>(RestoreProperty);
        set => SetValue(RestoreProperty, value);
    }

    /// <summary>What the application needs to recreate this pane, saved beside its id and handed to
    /// <see cref="DockingArea.PaneRestoring"/> on load; null for a pane declared in markup.</summary>
    public string RestoreKey { get; set; }

}
