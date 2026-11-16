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
    private readonly int cutAfterBytes;
    private readonly CancellationTokenSource stop = new();

    private readonly WebCredential signIn;
    private readonly string realm;
    private readonly bool isProxy;
    private int refusals;

    /// <summary>A site whose files are <paramref name="fileBytes"/> long, sent a piece every
    /// <paramref name="pieceDelayMs"/>; with <paramref name="cutAfterBytes"/>, the connection is dropped after that
    /// many, short of the length it said.</summary>
    public LocalSite(string html, int fileBytes = 64 * 1024, int pieceDelayMs = 0, int cutAfterBytes = 0)
        : this(html, null, null, false, fileBytes, pieceDelayMs, cutAfterBytes)
    {
    }

    private LocalSite(string html, WebCredential signIn, string realm, bool isProxy, int fileBytes = 64 * 1024,
        int pieceDelayMs = 0, int cutAfterBytes = 0)
    {
        page = Encoding.UTF8.GetBytes(html);
        this.signIn = signIn;
        this.realm = realm;
        this.isProxy = isProxy;
        this.fileBytes = fileBytes;
        this.pieceDelayMs = pieceDelayMs;
        this.cutAfterBytes = cutAfterBytes;
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Origin = "http://127.0.0.1:" + Port;
        _ = Serve();
    }

    /// <summary>A site that lets in only <paramref name="signIn"/>, asked with Basic for <paramref name="realm"/>: a
    /// request without it is refused with 401.</summary>
    public static LocalSite SigningIn(string html, WebCredential signIn, string realm) =>
        new(html, signIn, realm, false);

    /// <summary>A proxy that lets through only <paramref name="signIn"/>, asked with Basic for <paramref name="realm"/>:
    /// a request without it is refused with 407. Let through, it answers every address with the page itself.</summary>
    public static LocalSite ProxySigningIn(string html, WebCredential signIn, string realm) =>
        new(html, signIn, realm, true);

    /// <summary>The site's origin, its pages' addresses' start.</summary>
    public string Origin { get; }

    /// <summary>The port it listens on.</summary>
    public int Port { get; }

    /// <summary>How many requests it refused for want of the sign-in.</summary>
    public int Refusals => Volatile.Read(ref refusals);

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

            var text = Encoding.ASCII.GetString(request, 0, read);
            var line = text.Split("\r\n", 2)[0].Split(' ');
            var path = line.Length > 1 ? line[1] : "/";
            if (signIn != null && !IsSignedIn(text))
            {
                Interlocked.Increment(ref refusals);
                var status = isProxy ? "407 Proxy Authentication Required" : "401 Unauthorized";
                var header = isProxy ? "Proxy-Authenticate" : "WWW-Authenticate";
                var refusal = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\n{header}: Basic realm=\"{realm}\"\r\n" +
                                                      "Content-Type: text/html\r\nContent-Length: 0\r\n" +
                                                      "Connection: close\r\n\r\n");
                await stream.WriteAsync(refusal, stop.Token);
                return;
            }

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

    // Whether the request carries the sign-in, in the header a site or a proxy reads it from.
    private bool IsSignedIn(string request)
    {
        var header = isProxy ? "Proxy-Authorization: Basic " : "Authorization: Basic ";
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{signIn.UserName}:{signIn.Password}"));
        foreach (var headerLine in request.Split("\r\n"))
        {
            if (headerLine.StartsWith(header, StringComparison.OrdinalIgnoreCase) &&
                headerLine[header.Length..].Trim() == expected)
            {
                return true;
            }
        }

        return false;
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
                if (cutAfterBytes > 0 && sent >= cutAfterBytes)
                {
                    return;
                }

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
