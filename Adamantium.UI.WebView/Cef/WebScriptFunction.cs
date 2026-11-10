using System.Text;
using System.Text.Json;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A function a page handed an object of the application's, kept by the page under a number in the script
/// world it came from; called and let go of there, on the UI loop whatever thread asks.</summary>
internal sealed class WebScriptFunction : IWebScriptFunction
{
    private readonly WebViewBrowser browser;
    private readonly string context;
    private readonly int number;
    private readonly JsonSerializerOptions options;
    private int disposed;

    public WebScriptFunction(WebViewBrowser browser, string context, int number, JsonSerializerOptions options)
    {
        this.browser = browser;
        this.context = context;
        this.number = number;
        this.options = options;
    }

    public Task<string> CallAsync(params object[] arguments)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return Task.FromException<string>(new ObjectDisposedException(nameof(IWebScriptFunction)));
        }

        var script = new StringBuilder("window.adamantium.__call(").Append(number).Append(",[");
        for (var i = 0; i < (arguments?.Length ?? 0); i++)
        {
            if (i > 0)
            {
                script.Append(',');
            }

            var argument = arguments[i];
            script.Append(argument == null ? "null" : JsonSerializer.Serialize(argument, options.GetTypeInfo(argument.GetType())));
        }

        script.Append("])");
        if (LoopSignal.IsLoopThread)
        {
            return browser.EvaluateInAsync(context, script.ToString());
        }

        var called = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        LoopSignal.Post(() => browser.EvaluateInAsync(context, script.ToString()).ContinueWith(answer =>
        {
            if (answer.IsCompletedSuccessfully)
            {
                called.TrySetResult(answer.Result);
            }
            else
            {
                called.TrySetException(answer.Exception?.InnerExceptions ?? [new WebViewScriptException("The call failed.")]);
            }
        }, TaskScheduler.Default));
        return called.Task;
    }

    // One the application never disposed is let go of in the page once collected - the page would keep it otherwise.
    ~WebScriptFunction() => LetGo();

    public void Dispose()
    {
        LetGo();
        GC.SuppressFinalize(this);
    }

    private void LetGo()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var script = "window.adamantium&&window.adamantium.__letGo(" + number + ")";
        if (LoopSignal.IsLoopThread)
        {
            browser.EvaluateInAsync(context, script).ContinueWith(answer => _ = answer.Exception, TaskScheduler.Default);
        }
        else
        {
            LoopSignal.Post(() =>
                browser.EvaluateInAsync(context, script).ContinueWith(answer => _ = answer.Exception, TaskScheduler.Default));
        }
    }
}
