using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls;

/// <summary>The themed card hosting tooltip content (a string or an element), shown by
/// <see cref="ToolTipService"/>.</summary>
public class ToolTip : ContentControl
{
    static ToolTip()
    {
        FontSizeProperty.OverrideMetadata(typeof(ToolTip),
            new PropertyMetadata(12.0, PropertyMetadataOptions.Inherits | PropertyMetadataOptions.AffectsMeasure));
    }

}
