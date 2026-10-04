using Adamantium.MVVM;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A filter of the module catalog and how many modules it lets through.</summary>
[ViewModel]
public partial class CatalogFilterChoice
{
    [Bindable] private int _count;

    public ModuleCatalogFilter Filter { get; init; }
}
