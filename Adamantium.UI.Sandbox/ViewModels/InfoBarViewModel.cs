using Adamantium.MVVM;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>InfoBar tab: a message of each severity. Each bar's IsOpen is two-way bound to its toggle, so the bar's ×
/// button flows the closed state back to the switch and the switch brings the bar back.</summary>
[ViewModel]
public partial class InfoBarViewModel : TabPageViewModel
{
    public InfoBarViewModel() : base("InfoBar") { }

    [Bindable] private bool _errorOpen = true;
    [Bindable] private bool _warningOpen = true;
    [Bindable] private bool _informationalOpen = true;
    [Bindable] private bool _successOpen = true;
}
