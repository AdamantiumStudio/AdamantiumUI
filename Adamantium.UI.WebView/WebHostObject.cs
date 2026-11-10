namespace Adamantium.UI.WebView;

/// <summary>
/// An object of the application's a page calls, as the page sees it: its methods by their names in JavaScript. The
/// build makes one for every interface marked <see cref="WebHostObjectAttribute"/>; written by hand, it is any object
/// whose calls are read from <see cref="WebHostArguments"/> and answered with a <see cref="WebHostAnswer"/>.
/// </summary>
public abstract class WebHostObject
{
    /// <summary>Runs <paramref name="method"/>, named as the page calls it, with the page's arguments; throws for a
    /// method there is none of, and what it throws is what the page's promise is refused with.</summary>
    public abstract ValueTask<WebHostAnswer> InvokeAsync(string method, WebHostArguments arguments);

    /// <summary>What a call of a method there is none of throws.</summary>
    protected InvalidOperationException NoSuchMethod(string method) =>
        new($"There is no method '{method}' to call here.");

    /// <summary>The answer once <paramref name="task"/> is done: nothing.</summary>
    protected static async ValueTask<WebHostAnswer> When(Task task)
    {
        await task.ConfigureAwait(false);
        return WebHostAnswer.None;
    }

    /// <summary>The answer once <paramref name="task"/> is done: its value, as <paramref name="answer"/> makes it.
    /// </summary>
    protected static async ValueTask<WebHostAnswer> When<T>(Task<T> task, Func<T, WebHostAnswer> answer) =>
        answer(await task.ConfigureAwait(false));

    /// <summary>The answer once <paramref name="task"/> is done: nothing.</summary>
    protected static ValueTask<WebHostAnswer> When(ValueTask task) => When(task.AsTask());

    /// <summary>The answer once <paramref name="task"/> is done: its value, as <paramref name="answer"/> makes it.
    /// </summary>
    protected static ValueTask<WebHostAnswer> When<T>(ValueTask<T> task, Func<T, WebHostAnswer> answer) =>
        When(task.AsTask(), answer);
}
