using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.WebView.Tests;

/// <summary>A site on this machine for a test's pages, at an origin of its own: every address under it is answered
/// with the same page. Needs no network, unlike a site out there.</summary>
internal sealed class LocalSite : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly byte[] page;
    private readonly CancellationTokenSource stop = new();

    public LocalSite(string html)
    {
        page = Encoding.UTF8.GetBytes(html);
        listener.Start();
        Origin = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Serve();
    }

    /// <summary>The site's origin, its pages' addresses' start.</summary>
    public string Origin { get; }

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

            var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                                               $"Content-Length: {page.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, stop.Token);
            await stream.WriteAsync(page, stop.Token);
        }
    }
}
