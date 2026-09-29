using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Decorators;

public class Border : Decorator
{
   public static readonly AdamantiumProperty BorderBrushProperty = AdamantiumProperty.Register(nameof(BorderBrush),
      typeof (Brush), typeof (Border),
      new PropertyMetadata(Brushes.Transparent, PropertyMetadataOptions.AffectsRender));

   public static readonly AdamantiumProperty CornerRadiusProperty = AdamantiumProperty.Register(nameof(CornerRadius),
      typeof (CornerRadius), typeof (Border),
      new PropertyMetadata(default(CornerRadius), PropertyMetadataOptions.AffectsMeasure,
         (d, _) => NotifyOwnCornersChanged(d)));   // these corners are also the CLIP's, when it clips - see the note there

   public static readonly AdamantiumProperty BorderThicknessProperty =
      AdamantiumProperty.Register(nameof(BorderThickness),
         typeof (Thickness), typeof (Border),
         new PropertyMetadata(default(Thickness), PropertyMetadataOptions.AffectsMeasure));

   public static readonly AdamantiumProperty BackgroundProperty = AdamantiumProperty.Register(nameof(Background),
      typeof (Brush), typeof (Border),
      new PropertyMetadata(Brushes.Transparent, PropertyMetadataOptions.AffectsRender));

   public Brush BorderBrush
   {
      get => GetValue<Brush>(BorderBrushProperty);
      set => SetValue(BorderBrushProperty, value);
   }

   public Brush Background
   {
      get => GetValue<Brush>(BackgroundProperty);
      set => SetValue(BackgroundProperty, value);
   }

   public CornerRadius CornerRadius
   {
      get => GetValue<CornerRadius>(CornerRadiusProperty);
      set => SetValue(CornerRadiusProperty, value);
   }

   /// <inheritdoc />
   protected override Vector4F OwnCornerRadii()
   {
      var r = CornerRadius;
      return new Vector4F((float)r.TopLeft, (float)r.TopRight, (float)r.BottomRight, (float)r.BottomLeft);
   }

   public Thickness BorderThickness
   {
      get => GetValue<Thickness>(BorderThicknessProperty);
      set => SetValue(BorderThicknessProperty, value);
   }

   public Border()
   {
   }

   protected override Size MeasureOverride(Size availableSize)
   {
      var child = Child;
      var padding = Padding + BorderThickness;
      var size = availableSize.Deflate(padding);
      if (child != null)
      {
         child.Measure(size);
         return child.DesiredSize.Inflate(padding);
      }

      return new Size(padding.Left + padding.Right, padding.Bottom + padding.Top);
   }

   protected override Size ArrangeOverride(Size finalSize)
   {
      // Fill the arranged slot and lay the child out inside it (minus border+padding); the child positions itself within
      // via its own alignment. Returning the CHILD's size instead collapsed a stretched border to a non-stretch child -
      // e.g. a Button's chrome shrank to its centered ContentPresenter and the text spilled outside. Shrink-to-content is
      // a MEASURE concern (MeasureOverride already returns child+padding, honoring Width/Height), not arrange.
      var padding = Padding + BorderThickness;
      Child?.Arrange(new Rect(finalSize).Deflate(padding));
      return finalSize;
   }

   protected override void OnRender(IDrawingContext context)
   {
      var borderThickness = BorderThickness;
      var cornerRadius = CornerRadius;
      base.OnRender(context);

      var ctx = context.ForControl(this);

      var hasThickness = borderThickness.Left != 0 || borderThickness.Top != 0 || borderThickness.Right != 0 || borderThickness.Bottom != 0;

      // One SDF draw for fill and ring whatever the sides and corners, snapped to whole pixels together so they never
      // disagree along the edge.
      var box = new Rect(new Size(ActualWidth, ActualHeight));
      this.Snap(ref box);

      if (hasThickness && BorderBrush.IsVisible())
      {
         var t = borderThickness;
         if (t.IsUniform)
         {
            // A uniform border rounds its ONE thickness the same way the box was rounded, so the ring lands on whole
            // pixels too. Unequal sides have no single number to snap and keep what they were given.
            var one = t.Left;
            var ring = new Rect(new Size(ActualWidth, ActualHeight));
            this.Snap(ref ring, ref one);
            t = new Thickness(one);
         }

         var fill = Background.IsVisible() ? Background : Brushes.Transparent;
         ctx.DrawBorder(fill, box, cornerRadius, BorderBrush, t);
         return;
      }

      // No border: just the fill. Only record draws that produce visible pixels - a transparent brush would otherwise
      // still build a fill unit + fringe every frame (the ListBox FPS drop); hit-testing is bounds-based, so nothing
      // depends on the invisible draw.
      if (Background.IsVisible())
      {
         ctx.DrawRectangle(Background, box, cornerRadius);
      }
   }

}