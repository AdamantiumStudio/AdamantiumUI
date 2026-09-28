using Adamantium.MVVM;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Transforms tab: rotated and sheared SDF tiles that stay in one instanced draw, each reading its matrix from the
/// transform table.</summary>
[ViewModel]
public partial class TransformsViewModel : TabPageViewModel
{
    public TransformsViewModel() : base("Transforms") { }

    public TransformSettings Transforms { get; } = new();
}
