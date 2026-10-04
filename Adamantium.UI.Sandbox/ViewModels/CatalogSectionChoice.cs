using Adamantium.MVVM;
using Adamantium.UI.Sandbox.Modules;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A section of the module catalog and how many modules it holds.</summary>
[ViewModel]
public partial class CatalogSectionChoice
{
    [Bindable] private int _count;

    public ModuleSection Section { get; init; }
}
