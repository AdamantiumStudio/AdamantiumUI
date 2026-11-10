using System;
using Adamantium.MVVM;
using Adamantium.UI.WebView;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A page the Web tab's page opened - a link to a new tab, <c>window.open</c>, its page source - shown beside
/// it while the page that opened it goes on.</summary>
public partial class OpenedWebPageViewModel : AdamantiumViewModel, IDisposable
{
    public OpenedWebPageViewModel(WebViewNewPage page, WebNewWindowKind kind)
    {
        Page = page;
        Kind = kind;
        Title = page.Address;
    }

    /// <summary>The page, for the view that shows it.</summary>
    public WebViewNewPage Page { get; }

    /// <summary>How it was asked to open.</summary>
    public WebNewWindowKind Kind { get; }

    /// <summary>Where the page is.</summary>
    [Bindable] private Uri _source;

    /// <summary>The page's title, its address until it has one.</summary>
    [Bindable] private string _title;

    public void Dispose() => Page.Dispose();
}
