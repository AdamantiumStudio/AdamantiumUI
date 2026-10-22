using System;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Content of a window that lets clicks through to whatever is behind it for a few seconds, counting them down,
/// and then takes them again - so it can still be closed by its own button.</summary>
[ViewModel]
public partial class ClickThroughWindowViewModel : IWindowAware
{
    private const int PassSeconds = 10;

    private DispatcherTimer _countdown;
    private int _secondsLeft;

    [Bindable] private bool _clickThrough;
    [Bindable] private string _status = NavigationStrings.ClickThroughOff;

    public string WindowShellKey => "clickthrough";
    public string Title => NavigationStrings.ClickThroughWindow;
    public double Width => 420;
    public double Height => 240;

    [Command]
    private void PassClicks()
    {
        if (_countdown == null)
        {
            _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdown.Tick += OnCountdown;
        }

        _secondsLeft = PassSeconds;
        ClickThrough = true;
        Status = NavigationStrings.ClickThroughCountdown(_secondsLeft);
        _countdown.Start();
    }

    private void OnCountdown(object sender, EventArgs e)
    {
        _secondsLeft--;
        if (_secondsLeft > 0)
        {
            Status = NavigationStrings.ClickThroughCountdown(_secondsLeft);
            return;
        }

        _countdown.Stop();
        ClickThrough = false;
        Status = NavigationStrings.ClickThroughOff;
    }
}
