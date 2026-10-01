using Adamantium.MVVM;

namespace AdamantiumApp.ViewModels;

[ViewModel]
public partial class MainWindowViewModel
{
    private int _clicks;

    [Bindable]
    private string _greeting = "Hello, Adamantium!";

    [Command]
    private void Click()
    {
        _clicks++;
        Greeting = $"Clicked {_clicks} times";
    }
}
