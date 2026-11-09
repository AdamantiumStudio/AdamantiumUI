using System.Net.WebSockets;
using System.Threading.Channels;

namespace Adamantium.UI.WebView.Cef;

/// <summary>One DevTools front end's connection to a <see cref="WebViewDevToolsSocket"/>, with the messages of the page
/// waiting for it - no more than so many: a front end that stops reading is let go rather than the messages piling up.
/// </summary>
internal sealed class WebViewDevToolsConnection(WebSocket socket)
{
    private const int MaxWaitingMessages = 10_000;

    /// <summary>The front end's WebSocket.</summary>
    public WebSocket Socket { get; } = socket;

    /// <summary>The page's messages on their way to the front end.</summary>
    public Channel<string> Outgoing { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(MaxWaitingMessages)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });

    /// <summary>Ends the connection: nothing more goes out, the socket is let go.</summary>
    public void Close()
    {
        Outgoing.Writer.TryComplete();
        Socket.Abort();
        Socket.Dispose();
    }
}
