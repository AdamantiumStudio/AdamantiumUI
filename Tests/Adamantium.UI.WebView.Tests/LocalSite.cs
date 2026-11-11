using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.WebView.Tests;

/// <summary>A site on this machine for a test's pages, at an origin of its own: every address under it is answered
/// with the same page - but one under <c>/download/</c>, a file sent as an attachment, named as the address ends, in
/// pieces a while apart. Needs no network, unlike a site out there.</summary>
internal sealed class LocalSite : IDisposable
{
    private const int PieceBytes = 16 * 1024;

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly byte[] page;
    private readonly int fileBytes;
    private readonly int pieceDelayMs;
    private readonly CancellationTokenSource stop = new();

    public LocalSite(string html, int fileBytes = 64 * 1024, int pieceDelayMs = 0)
    {
        page = Encoding.UTF8.GetBytes(html);
        this.fileBytes = fileBytes;
        this.pieceDelayMs = pieceDelayMs;
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

            var line = Encoding.ASCII.GetString(request, 0, read).Split("\r\n", 2)[0].Split(' ');
            var path = line.Length > 1 ? line[1] : "/";
            if (path.StartsWith("/download/", StringComparison.Ordinal))
            {
                await SendFile(stream, path["/download/".Length..]);
                return;
            }

            var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                                               $"Content-Length: {page.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, stop.Token);
            await stream.WriteAsync(page, stop.Token);
        }
    }

    // The file's bytes are its offsets, a byte each - checked as they land.
    private async Task SendFile(NetworkStream stream, string name)
    {
        var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n" +
                                           $"Content-Disposition: attachment; filename=\"{name}\"\r\n" +
                                           $"Content-Length: {fileBytes}\r\nConnection: close\r\n\r\n");
        try
        {
            await stream.WriteAsync(head, stop.Token);
            var piece = new byte[PieceBytes];
            for (var sent = 0; sent < fileBytes; sent += piece.Length)
            {
                var count = Math.Min(piece.Length, fileBytes - sent);
                for (var i = 0; i < count; i++)
                {
                    piece[i] = (byte)(sent + i);
                }

                await stream.WriteAsync(piece.AsMemory(0, count), stop.Token);
                if (pieceDelayMs > 0)
                {
                    await Task.Delay(pieceDelayMs, stop.Token);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The browser cancelled, or the site stopped.
        }
    }
}
