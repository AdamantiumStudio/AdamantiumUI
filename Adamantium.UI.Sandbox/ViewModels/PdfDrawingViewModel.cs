using System;
using System.Collections.Generic;
using Adamantium.MVVM;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>PDF drawing tab: groups of draws inside one element - cut by a path, a rectangle and letters, through soft
/// masks, nested, and every blend mode.</summary>
[ViewModel]
public partial class PdfDrawingViewModel : TabPageViewModel
{
    public PdfDrawingViewModel() : base("PdfDrawing") { }

    /// <summary>Every blend mode, a sample each.</summary>
    public IReadOnlyList<BlendMode> BlendModes { get; } = Enum.GetValues<BlendMode>();
}
