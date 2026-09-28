using Adamantium.Mathematics;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media;

public class Transform : AnimatableUIComponent
{
    public static readonly AdamantiumProperty ScaleXProperty = AdamantiumProperty.Register(nameof(ScaleX),
        typeof (Double), typeof (Transform), new PropertyMetadata(1.0, TransformPropertyChangedCallback));
        
    public static readonly AdamantiumProperty ScaleYProperty = AdamantiumProperty.Register(nameof(ScaleY),
        typeof (Double), typeof (Transform), new PropertyMetadata(1.0, TransformPropertyChangedCallback));
        
    public static readonly AdamantiumProperty RotationAngleProperty = AdamantiumProperty.Register(nameof(RotationAngle),
        typeof (Double), typeof (Transform), new PropertyMetadata(default(Double), TransformPropertyChangedCallback));

    // 3D rotations (degrees) around the X / Y axes through the rotation centre - the flip/tilt-tile effects. They fold
    // into the same single matrix (the render's transform table applies full 4x4s, so a 3D-rotated element STAYS in the
    // instanced batches). Perspective adds the depth foreshortening (see PerspectiveProperty).
    public static readonly AdamantiumProperty RotationXProperty = AdamantiumProperty.Register(nameof(RotationX),
        typeof (Double), typeof (Transform), new PropertyMetadata(default(Double), TransformPropertyChangedCallback));

    public static readonly AdamantiumProperty RotationYProperty = AdamantiumProperty.Register(nameof(RotationY),
        typeof (Double), typeof (Transform), new PropertyMetadata(default(Double), TransformPropertyChangedCallback));

    /// <summary>Camera distance (logical px) for 3D depth foreshortening; 0 (default) = no perspective (affine). Applied
    /// around the rotation centre, so a tile flips "in place" like WPF's classic 3D tile demos.</summary>
    public static readonly AdamantiumProperty PerspectiveProperty = AdamantiumProperty.Register(nameof(Perspective),
        typeof (Double), typeof (Transform), new PropertyMetadata(default(Double), TransformPropertyChangedCallback));
        
    public static readonly AdamantiumProperty RotationCenterXProperty = AdamantiumProperty.Register(nameof(RotationCenterX),
        typeof (Double), typeof (Transform), new PropertyMetadata(default(Double), TransformPropertyChangedCallback));
        
    public static readonly AdamantiumProperty RotationCenterYProperty = AdamantiumProperty.Register(nameof(RotationCenterY),
        typeof (Double), typeof (Transform), new PropertyMetadata(default(Double), TransformPropertyChangedCallback));
        
    public static readonly AdamantiumProperty TranslateXProperty = AdamantiumProperty.Register(nameof(TranslateX),
        typeof (Double), typeof (Transform), new PropertyMetadata(default(Double), TransformPropertyChangedCallback));
        
    public static readonly AdamantiumProperty TranslateYProperty = AdamantiumProperty.Register(nameof(TranslateY),
        typeof (Double), typeof (Transform), new PropertyMetadata(default(Double), TransformPropertyChangedCallback));

    /// <summary>Shear angles (degrees) about the rotation centre - <see cref="SkewX"/> slants horizontally with y (the
    /// "italic" lean), <see cref="SkewY"/> vertically with x. WPF's SkewTransform AngleX/AngleY; folded into the same
    /// single matrix, so a sheared element stays in the instanced batches like any other.</summary>
    public static readonly AdamantiumProperty SkewXProperty = AdamantiumProperty.Register(nameof(SkewX),
        typeof (Double), typeof (Transform), new PropertyMetadata(default(Double), TransformPropertyChangedCallback));

    public static readonly AdamantiumProperty SkewYProperty = AdamantiumProperty.Register(nameof(SkewY),
        typeof (Double), typeof (Transform), new PropertyMetadata(default(Double), TransformPropertyChangedCallback));

    private static void TransformPropertyChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is Transform transform)
        {
            transform.UpdateTransform();
        }
    }

    public Double ScaleX
    {
        get => GetValue<Double>(ScaleXProperty);
        set => SetValue(ScaleXProperty, value);
    }
        
    public Double ScaleY
    {
        get => GetValue<Double>(ScaleYProperty);
        set => SetValue(ScaleYProperty, value);
    }
        
    public Double RotationAngle
    {
        get => GetValue<Double>(RotationAngleProperty);
        set => SetValue(RotationAngleProperty, value);
    }

    public Double RotationX
    {
        get => GetValue<Double>(RotationXProperty);
        set => SetValue(RotationXProperty, value);
    }

    public Double RotationY
    {
        get => GetValue<Double>(RotationYProperty);
        set => SetValue(RotationYProperty, value);
    }

    public Double Perspective
    {
        get => GetValue<Double>(PerspectiveProperty);
        set => SetValue(PerspectiveProperty, value);
    }
        
    public Double TranslateX
    {
        get => GetValue<Double>(TranslateXProperty);
        set => SetValue(TranslateXProperty, value);
    }

    public Double TranslateY
    {
        get => GetValue<Double>(TranslateYProperty);
        set => SetValue(TranslateYProperty, value);
    }
        
    public Double SkewX
    {
        get => GetValue<Double>(SkewXProperty);
        set => SetValue(SkewXProperty, value);
    }

    public Double SkewY
    {
        get => GetValue<Double>(SkewYProperty);
        set => SetValue(SkewYProperty, value);
    }

    public Double RotationCenterX
    {
        get => GetValue<Double>(RotationCenterXProperty);
        set => SetValue(RotationCenterXProperty, value);
    }
        
    public Double RotationCenterY
    {
        get => GetValue<Double>(RotationCenterYProperty);
        set => SetValue(RotationCenterYProperty, value);
    }
        
    public Matrix4x4 Matrix { get; private set; } = Matrix4x4.Identity;

    /// <summary>The element this transform is assigned to as RenderTransform (set by the owner). Lets a transform tick
    /// mark ONLY its owner when the owner is a render MOTION NODE (its slot matrix rewrites - the O(1) tilt/flip path)
    /// instead of the global transform flag that re-bakes the whole scene.</summary>
    internal IUIComponent Owner
    {
        get => _owner;
        set
        {
            if (ReferenceEquals(_owner, value)) return;

            if (_owner is AdamantiumComponent previous) previous.PropertyChanged -= OnOwnerPropertyChanged;
            _owner = value;

            // ...and the INHERITANCE parent, which is what gives a transform a DataContext. A transform is a component
            // with the property system but no place in the tree, so on its own a {Binding} on it has nothing to resolve
            // against; through the owner it binds like anything else - which is what makes
            // <Transform ScaleX="{Binding Zoom}"/> - the obvious markup - actually work.
            InheritanceParent = value as AdamantiumComponent;
            if (value is AdamantiumComponent owner) owner.PropertyChanged += OnOwnerPropertyChanged;

            // A transform is BUILT before it is assigned, so its bindings were established with no owner and therefore
            // no DataContext - and nothing else would ever re-run them: a transform has no attach event of its own and
            // never sees a DataContext change. Re-establish here, and again below when the owner's DataContext arrives
            // (the usual order: build the tree, then hand it its data).
            Data.BindingEngine.RefreshBindings(this);
        }
    }

    private IUIComponent _owner;

    private void OnOwnerPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property?.Name == "DataContext") Data.BindingEngine.RefreshBindings(this);
    }

    /// <summary>True when this transform is an element's LayoutTransform (not its RenderTransform): a value change then
    /// re-runs the owner's LAYOUT, because it reshapes the footprint - not just the render.</summary>
    internal bool IsLayoutTransform { get; set; }

    private void UpdateTransform()
    {
        Matrix = CalculateFinalTransform();

        // A LayoutTransform reshapes the owner's FOOTPRINT, so a value change (AUML setting ScaleX after the property, an
        // animated zoom, ...) must re-run LAYOUT: measure re-cascades into arrange and the render. A RenderTransform only
        // moves an already-laid-out element, so it falls through to the render mark below.
        if (IsLayoutTransform)
        {
            if (Owner is IMeasurableComponent measurable) measurable.InvalidateMeasure();
            return;
        }

        // The render thread is drawing this matrix, so this write is only a mirror; if it has not applied it recently,
        // fall through and re-bake.
        if (Media.Animation.Compositor.EntryFor(this) is { AppliedRecently: true }) return;

        // A transform moves its owner without changing geometry: mark just the motion node, else the global Transform
        // mark (which, with no owner, re-captures the snapshot).
        if (Owner is { IsRenderMotionNode: true } node) RenderDirty.MarkNodeTransform(node);
        else RenderDirty.MarkTransform(Owner);
    }

    /// <summary>This transform's values as plain data - what the compositor captures so it can compose the matrix on the
    /// render thread without touching the property system. Read on the loop thread, at handoff.</summary>
    public TransformValues Values => new()
    {
        ScaleX = ScaleX,
        ScaleY = ScaleY,
        RotationAngle = RotationAngle,
        RotationX = RotationX,
        RotationY = RotationY,
        Perspective = Perspective,
        RotationCenterX = RotationCenterX,
        RotationCenterY = RotationCenterY,
        TranslateX = TranslateX,
        TranslateY = TranslateY,
        SkewX = SkewX,
        SkewY = SkewY
    };

    // The matrix arithmetic itself lives in TransformValues, so the compositor composes the SAME matrix from the SAME code -
    // two implementations of this would be two chances to disagree about where an element is.
    private Matrix4x4 CalculateFinalTransform() => Values.ToMatrix();
}