using System;
using System.Globalization;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Sandbox.Converters;

/// <summary>Shows a noise-panel control group only for noise types that use it. Keys: "Scale", "Seed" (all but
/// CombustibleVoronoi), "Fbm" (FBM types), "FirePalette" (CombustibleVoronoi only).</summary>
public class NoiseParamVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var type = value is NoiseType t ? t : NoiseType.Simplex;
        var visible = (parameter as string) switch
        {
            "Scale" => type != NoiseType.CombustibleVoronoi,
            "Seed" => type != NoiseType.CombustibleVoronoi,
            "Fbm" => type != NoiseType.VoronoiBorders && type != NoiseType.CombustibleVoronoi,
            "FirePalette" => type == NoiseType.CombustibleVoronoi,
            _ => true
        };
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
}
