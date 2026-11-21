using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>A site on this machine that lets in one user name and password, asked with Basic: a request without them is
/// refused with 401, one with them gets a page titled "inside". Needs no network.</summary>
internal sealed class SigningInSite : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly string expected;
    private int admitted;

    public SigningInSite(string userName, string password)
    {
        expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(userName + ":" + password));
        listener.Start();
        Origin = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Serve();
    }

    /// <summary>The site's origin.</summary>
    public string Origin { get; }

    /// <summary>How many requests came with the right user name and password.</summary>
    public int Admitted => Volatile.Read(ref admitted);

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
    }

    private async Task Serve()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Answer(client);
        }
    }

    private async Task Answer(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = new byte[8192];
                var read = 0;
                while (read < request.Length)
                {
                    var count = await stream.ReadAsync(request.AsMemory(read), stop.Token);
                    if (count == 0)
                    {
                        return;
                    }

                    read += count;
                    if (Encoding.ASCII.GetString(request, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                var text = Encoding.ASCII.GetString(request, 0, read);
                var path = text.Split("\r\n", 2)[0].Split(' ') is { Length: > 1 } parts ? parts[1] : "/";
                if (path == LoadingPath)
                {
                    await SendLoadingPage(stream);
                    return;
                }

                if (path == SlowScriptPath)
                {
                    await SendSlowScript(stream);
                    return;
                }

                var signedIn = false;
                foreach (var line in text.Split("\r\n"))
                {
                    if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase) &&
                        line["Authorization:".Length..].Trim() == expected)
                    {
                        signedIn = true;
                    }
                }

                string answer;
                if (signedIn)
                {
                    Interlocked.Increment(ref admitted);
                    const string page = "<title>inside</title><body>inside</body>";
                    answer = "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n" +
                             $"Content-Length: {page.Length}\r\nConnection: close\r\n\r\n{page}";
                }
                else
                {
                    answer = "HTTP/1.1 401 Unauthorized\r\nWWW-Authenticate: Basic realm=\"Lab\"\r\n" +
                             "Content-Length: 0\r\nConnection: close\r\n\r\n";
                }

                await stream.WriteAsync(Encoding.ASCII.GetBytes(answer), stop.Token);
            }
            catch (Exception exception) when (exception is System.IO.IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The browser went, or the site stopped.
            }
        }
    }

    private const string LoadingPath = "/loading";
    private const string SlowScriptPath = "/slow.js";

    private int scriptsAsked;
    private int scriptsSent;

    /// <summary>A page asking nothing that is in at once and loading for a few seconds after: its script comes in pieces.
    /// </summary>
    public string LoadingPage => Origin + LoadingPath;

    /// <summary>Whether the loading page's script was asked for: the page is in, its document read.</summary>
    public bool IsLoadingPageIn => Volatile.Read(ref scriptsAsked) > 0;

    /// <summary>Whether the loading page's script was sent whole: the page's load is ending.</summary>
    public bool IsLoadingPageSent => Volatile.Read(ref scriptsSent) > 0;

    private async Task SendLoadingPage(NetworkStream stream)
    {
        var page = $"<title>loading</title><body><script src=\"{SlowScriptPath}\"></script></body>";
        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n" +
                                                        $"Content-Length: {page.Length}\r\nConnection: close\r\n\r\n{page}"),
            stop.Token);
    }

    // Thirty-one pieces a fifth of a second apart: the page goes on loading for six seconds, room for a slow machine to
    // ask the next page's question meanwhile.
    private async Task SendSlowScript(NetworkStream stream)
    {
        Interlocked.Increment(ref scriptsAsked);
        const int pieces = 31;
        var piece = Encoding.ASCII.GetBytes(new string(' ', 1023) + "\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/javascript\r\n" +
                                                        $"Content-Length: {pieces * piece.Length}\r\nConnection: close\r\n\r\n"),
            stop.Token);
        for (var i = 0; i < pieces; i++)
        {
            if (i > 0)
            {
                await Task.Delay(200, stop.Token);
            }

            await stream.WriteAsync(piece, stop.Token);
        }

        Interlocked.Increment(ref scriptsSent);
    }
}
