using System.ComponentModel.DataAnnotations;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Which stand the layout tab is showing: one at a time, each a view of the same view-model.</summary>
public enum LayoutStand
{
    /// <summary>Sixty thousand tiles in a virtualized wrap grid, or paged.</summary>
    [Display(Name = "Tiles")]
    Tiles,

    /// <summary>The loading skeleton cards, held on screen and tuned by hand.</summary>
    [Display(Name = "Skeletons")]
    Skeletons,

    /// <summary>An expander opening each of the four ways.</summary>
    [Display(Name = "Expanders")]
    Expanders
}
