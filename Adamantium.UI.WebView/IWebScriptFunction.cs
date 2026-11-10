namespace Adamantium.UI.WebView;

/// <summary>A function a page handed an object of the application's: called in the page, while the page's document
/// lasts. Disposed, the page lets go of it.</summary>
public interface IWebScriptFunction : IDisposable
{
    /// <summary>Calls the function with <paramref name="arguments"/>, written as JSON; its value as JSON (a promise is
    /// waited for). Throws <see cref="WebViewScriptException"/> when it throws, or when its document is gone.</summary>
    Task<string> CallAsync(params object[] arguments);
}
