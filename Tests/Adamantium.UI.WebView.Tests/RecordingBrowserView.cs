using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView.Tests;

/// <summary>A view a test makes a browser for: it keeps what the browser tells it.</summary>
internal sealed class RecordingBrowserView : IWebViewBrowserView
{
    /// <summary>The surfaces shown last; null once the browser let go of them.</summary>
    public SharedSurfaceDescriptor Shown { get; private set; }

    /// <summary>The frames written.</summary>
    public int Frames { get; private set; }

    /// <summary>Whether the browser's frames could not be copied from Chromium's texture.</summary>
    public bool Refused { get; private set; }

    /// <summary>The cursor the page asked for last.</summary>
    public Cursor Cursor { get; private set; }

    /// <summary>What the page started dragging last; null before it did.</summary>
    public DataPackage Dragged { get; private set; }

    /// <summary>The page's address, as the browser said last.</summary>
    public string Address { get; private set; }

    /// <summary>The page's title, as the browser said last.</summary>
    public string Title { get; private set; }

    /// <summary>Whether the page is loading, and can go back and forward, as the browser said last.</summary>
    public bool IsLoading { get; private set; }

    public bool CanGoBack { get; private set; }

    public bool CanGoForward { get; private set; }

    /// <summary>The addresses the page was about to go to, in order.</summary>
    public List<string> Started { get; } = [];

    /// <summary>The addresses the page loaded or failed to, in order.</summary>
    public List<string> Completed { get; } = [];

    /// <summary>The messages the page sent, as JSON.</summary>
    public List<string> Messages { get; } = [];

    /// <summary>Says which addresses the page is kept from going to.</summary>
    public Func<string, bool> Refuse { get; set; }

    /// <summary>The page's zoom the browser gave last.</summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>The status text the browser gave last.</summary>
    public string Status { get; private set; }

    /// <summary>The load error the browser gave last; null once it said the error was over.</summary>
    public WebViewLoadError Error { get; private set; }

    /// <summary>How many times the page started loading.</summary>
    public int LoadsStarted { get; private set; }

    public void Show(WebViewBrowser browser, SharedSurfaceDescriptor descriptor) => Shown = descriptor;

    public void LetGo(WebViewBrowser browser) => Shown = null;

    public void FrameWritten(WebViewBrowser browser) => Frames++;

    public void FrameRate(WebViewBrowser browser, int rate)
    {
    }

    public void SharedTextureRefused(WebViewBrowser browser) => Refused = true;

    public void CursorChanged(WebViewBrowser browser, Cursor cursor) => Cursor = cursor;

    public void CompositionBoundsChanged(WebViewBrowser browser, Rect bounds)
    {
    }

    /// <summary>The page's icon as last told; null for none.</summary>
    public ImageSource Icon { get; private set; }

    /// <summary>Whether the page was last told secure.</summary>
    public bool IsSecure { get; private set; }

    public void IconChanged(WebViewBrowser browser, ImageSource icon) => Icon = icon;

    public void SecurityChanged(WebViewBrowser browser, bool isSecure) => IsSecure = isSecure;

    /// <summary>The page's tooltip as last told.</summary>
    public string Tooltip { get; private set; }

    /// <summary>The menus the page asked for, in order.</summary>
    public List<WebViewContextMenu> Menus { get; } = [];

    public void TooltipChanged(WebViewBrowser browser, string text) => Tooltip = text;

    public bool ContextMenuRequested(WebViewBrowser browser, WebViewContextMenu menu)
    {
        Menus.Add(menu);
        return true;
    }

    public void AddressChanged(WebViewBrowser browser, string address) => Address = address;

    public void TitleChanged(WebViewBrowser browser, string title) => Title = title;

    public void LoadingChanged(WebViewBrowser browser, bool isLoading, bool canGoBack, bool canGoForward)
    {
        if (isLoading && !IsLoading)
        {
            LoadsStarted++;
        }

        IsLoading = isLoading;
        CanGoBack = canGoBack;
        CanGoForward = canGoForward;
    }

    public void ProgressChanged(WebViewBrowser browser, double progress)
    {
    }

    public void StatusChanged(WebViewBrowser browser, string status) => Status = status;

    public void ZoomChanged(WebViewBrowser browser, double factor) => Zoom = factor;

    public bool NavigationStarting(WebViewBrowser browser, string address, bool userGesture, bool isRedirect)
    {
        Started.Add(address);
        return Refuse?.Invoke(address) == true;
    }

    public void NavigationCompleted(WebViewBrowser browser, string address, WebViewLoadError error) =>
        Completed.Add(address);

    public void MessageReceived(WebViewBrowser browser, string json) => Messages.Add(json);

    public void LoadFailed(WebViewBrowser browser, WebViewLoadError error) => Error = error;

    public bool StartDragging(WebViewBrowser browser, DataPackage data, DragDropEffects allowed)
    {
        Dragged = data;
        return false;
    }
}
