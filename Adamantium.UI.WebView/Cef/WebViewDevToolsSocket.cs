using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// Where a page's DevTools front end talks to the page: a WebSocket on this machine only, on a port of the system's
/// choosing, at an address with a key no one else knows, from the DevTools front end. One front end at a time - a new
/// one, its page reloaded, takes the place of the old. What comes in is handed to the page's DevTools protocol on the UI
/// loop, as is word that a front end went; what the page answers and tells goes out.
/// </summary>
internal sealed class WebViewDevToolsSocket : IDisposable
{
    private const string FrontEndOrigin = "devtools://devtools";
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const int MaxRequestBytes = 16 * 1024;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);

    private readonly TcpListener listener;
    private readonly byte[] path;
    private readonly Action<string> toPage;
    private readonly Action frontEndGone;
    private readonly CancellationTokenSource stop = new();
    private readonly Lock connectionLock = new();
    private WebViewDevToolsConnection current;

    /// <summary>Listens for the front end: what it sends reaches <paramref name="toPage"/>, and its going
    /// <paramref name="frontEndGone"/>, on the UI loop.</summary>
    public WebViewDevToolsSocket(Action<string> toPage, Action frontEndGone)
    {
        this.toPage = toPage;
        this.frontEndGone = frontEndGone;
        var address = "/devtools/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        path = Encoding.ASCII.GetBytes(address);
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(8);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        // The desktop front end, not inspector.html: that one is for a remote device and shows the page again beside it.
        FrontEndAddress = $"{FrontEndOrigin}/bundled/devtools_app.html?ws=127.0.0.1:{port}{address}";
        _ = AcceptAsync();
    }

    /// <summary>The address of the DevTools front end, told where to connect.</summary>
    public string FrontEndAddress { get; }

    /// <summary>Hands the front end a message of the page's protocol, as JSON; nothing while none is connected. A front
    /// end that stops reading while the page goes on telling is let go rather than the messages piling up.</summary>
    public void Send(string json)
    {
        WebViewDevToolsConnection connection;
        lock (connectionLock)
        {
            connection = current;
        }

        if (connection != null && !connection.Outgoing.Writer.TryWrite(json))
        {
            connection.Socket.Abort();
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        lock (connectionLock)
        {
            current?.Socket.Abort();
            current = null;
        }
    }

    private async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or
                                                  ObjectDisposedException)
            {
                return;
            }

            _ = ServeAsync(client);
        }
    }

    // A client of its own: a slow or broken one keeps no other out.
    private async Task ServeAsync(TcpClient client)
    {
        WebViewDevToolsConnection connection = null;
        try
        {
            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                handshake.CancelAfter(HandshakeTimeout);
                var socket = await TryUpgrade(client.GetStream(), handshake.Token);
                if (socket == null)
                {
                    return;
                }

                connection = new WebViewDevToolsConnection(socket);
            }

            WebViewDevToolsConnection replaced;
            lock (connectionLock)
            {
                replaced = current;
                current = connection;
            }

            if (replaced != null)
            {
                replaced.Socket.Abort();
                PostFrontEndGone();
            }

            var sending = SendAsync(connection);
            await ReceiveAsync(connection);
            await sending;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or
                                              ObjectDisposedException or IOException or WebSocketException)
        {
        }
        finally
        {
            if (connection != null)
            {
                var wasCurrent = false;
                lock (connectionLock)
                {
                    if (ReferenceEquals(current, connection))
                    {
                        current = null;
                        wasCurrent = true;
                    }
                }

                connection.Close();
                if (wasCurrent)
                {
                    PostFrontEndGone();
                }
            }

            client.Dispose();
        }
    }

    // The HTTP request that asks for a WebSocket, answered only at our address and from the DevTools front end; the
    // connection it makes, or null.
    private async Task<WebSocket> TryUpgrade(NetworkStream stream, CancellationToken token)
    {
        var request = new StringBuilder();
        var buffer = new byte[1024];
        while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, token);
            if (read == 0 || request.Length + read > MaxRequestBytes)
            {
                return null;
            }

            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        var lines = request.ToString().Split("\r\n");
        var requestLine = lines[0].Split(' ');
        string key = null;
        string origin = null;
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name.Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
            {
                key = value;
            }
            else if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase))
            {
                origin = value;
            }
        }

        var askedPath = requestLine.Length >= 2 ? Encoding.ASCII.GetBytes(requestLine[1]) : [];
        if (requestLine.Length < 2 || requestLine[0] != "GET" || key == null ||
            !CryptographicOperations.FixedTimeEquals(askedPath, path) ||
            (origin != null && origin != FrontEndOrigin))
        {
            await stream.WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), token);
            return null;
        }

        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
        var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                       $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), token);
        return WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true });
    }

    private async Task ReceiveAsync(WebViewDevToolsConnection connection)
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        while (connection.Socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
        {
            var result = await connection.Socket.ReceiveAsync(buffer, stop.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            LoopSignal.Post(() =>
            {
                if (!stop.IsCancellationRequested && IsCurrent(connection))
                {
                    toPage(json);
                }
            });
        }
    }

    private async Task SendAsync(WebViewDevToolsConnection connection)
    {
        try
        {
            await foreach (var json in connection.Outgoing.Reader.ReadAllAsync(stop.Token))
            {
                await connection.Socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true,
                    stop.Token);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or
                                              ObjectDisposedException or IOException)
        {
            connection.Socket.Abort();
        }
    }

    // Queued as the front end's own messages are, so the page hears of the old one going before the new one speaks.
    private void PostFrontEndGone() =>
        LoopSignal.Post(() =>
        {
            if (!stop.IsCancellationRequested)
            {
                frontEndGone();
            }
        });

    private bool IsCurrent(WebViewDevToolsConnection connection)
    {
        lock (connectionLock)
        {
            return ReferenceEquals(current, connection);
        }
    }
}
