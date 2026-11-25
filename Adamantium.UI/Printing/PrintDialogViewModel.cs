using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Navigation;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Themes.Localization;

namespace Adamantium.UI.Printing;

/// <summary>The print dialog as the dialog service shows it: what is printed and by whom, handed over when it opens, and
/// how it was left, handed back. The dialog itself - its options, its preview - is the <see cref="Controls.Printing.PrintDialog"/>
/// control its view holds.</summary>
internal sealed class PrintDialogViewModel : IDialogAware, IWindowAware
{
    internal const string DocumentKey = "document";
    internal const string PlatformKey = "platform";
    internal const string CurrentPageKey = "currentPage";
    internal const string SettingsKey = "settings";

    public IPrintDocument Document { get; private set; }

    public IPrintPlatform Platform { get; private set; }

    public int CurrentPage { get; private set; }

    public string Title => PrintStrings.Print;

    public string WindowShellKey => null;

    public double Width => 980;

    public double Height => 680;

    public event Action<IDialogResult> RequestClose;

    public Task OnDialogOpenedAsync(NavigationParameters parameters, CancellationToken cancellationToken = default)
    {
        Document = parameters?.GetValue<IPrintDocument>(DocumentKey);
        Platform = parameters?.GetValue<IPrintPlatform>(PlatformKey);
        CurrentPage = parameters?.GetValue(CurrentPageKey, 0) ?? 0;
        return Task.CompletedTask;
    }

    public Task<bool> CanCloseDialogAsync() => Task.FromResult(true);

    /// <summary>Closes the dialog to print <paramref name="settings"/>.</summary>
    public void Print(PrintSettings settings) =>
        RequestClose?.Invoke(DialogResult.Ok(new NavigationParameters().Add(SettingsKey, settings)));

    public void Cancel() => RequestClose?.Invoke(DialogResult.Cancel());
}
