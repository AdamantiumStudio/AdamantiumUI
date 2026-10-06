using System.Collections.ObjectModel;
using System.Linq;
using Adamantium.MVVM;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Instancing tab: a virtualized grid of identical tessellated stars, which collapse into one instanced draw of a
/// shared mesh with per-instance transform and color.</summary>
[ViewModel]
public partial class InstancingViewModel : TabPageViewModel
{
    public InstancingViewModel() : base("Instancing") { }

    private static readonly string[] Palette =
        ["#3B82F6", "#22C55E", "#F59E0B", "#EF4444", "#8B5CF6", "#14B8A6", "#EC4899", "#EAB308"];

    public ObservableCollection<ColorRect> Stars { get; } =
        new(Enumerable.Range(0, 600).Select(i => new ColorRect { Number = i + 1, Color = Palette[i % Palette.Length] }));
}
