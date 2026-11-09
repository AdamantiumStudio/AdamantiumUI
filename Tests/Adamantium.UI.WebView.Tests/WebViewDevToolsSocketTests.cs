using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Adamantium.UI.Core;
using Adamantium.UI.WebView.Cef;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

/// <summary>The socket a page's DevTools front end talks through: only at its own address, only from the DevTools
/// front end, its messages handed to the page on the UI loop. No CEF needed.</summary>
[TestFixture]
[NonParallelizable]
public class WebViewDevToolsSocketTests
{
    [Test]
    public void TheFrontEnd_AtTheAddress_ReachesThePage()
    {
        var received = new List<string>();
        using var socket = new WebViewDevToolsSocket(received.Add, () => { });
        using var client = Connect(socket, SocketAddress(socket), "devtools://devtools");

        client.SendAsync(Encoding.UTF8.GetBytes("{\"id\":7}"), WebSocketMessageType.Text, true, CancellationToken.None)
            .Wait(5000);
        SpinWait.SpinUntil(() =>
        {
            LoopSignal.Drain();
            return received.Count > 0;
        }, 5000);

        Assert.That(received, Is.EqualTo(new[] { "{\"id\":7}" }));
    }

    // A client that never finishes asking, or breaks off, keeps no front end out.
    [Test]
    public void ABrokenClient_KeepsNoFrontEndOut()
    {
        var received = new List<string>();
        using var socket = new WebViewDevToolsSocket(received.Add, () => { });
        var address = SocketAddress(socket);
        using var silent = new System.Net.Sockets.TcpClient("127.0.0.1", address.Port);
        using (var broken = new System.Net.Sockets.TcpClient("127.0.0.1", address.Port))
        {
            broken.Client.Send("GET /x"u8.ToArray());
            broken.Client.LingerState = new System.Net.Sockets.LingerOption(true, 0);
        }

        using var client = Connect(socket, address, "devtools://devtools");

        Assert.That(client.State, Is.EqualTo(WebSocketState.Open));
    }

    // A new front end - its page reloaded - takes the place of the old one, and the page hears that the old one went.
    [Test]
    public void ANewFrontEnd_TakesTheOldOnesPlace()
    {
        var gone = 0;
        using var socket = new WebViewDevToolsSocket(_ => { }, () => gone++);
        using var first = Connect(socket, SocketAddress(socket), "devtools://devtools");
        using var second = Connect(socket, SocketAddress(socket), "devtools://devtools");
        SpinWait.SpinUntil(() =>
        {
            LoopSignal.Drain();
            return gone > 0;
        }, 5000);

        Assert.Multiple(() =>
        {
            Assert.That(second.State, Is.EqualTo(WebSocketState.Open));
            Assert.That(gone, Is.EqualTo(1), "the page heard the first one go");
        });
    }

    [Test]
    public void AnotherAddress_IsTurnedAway()
    {
        using var socket = new WebViewDevToolsSocket(_ => { }, () => { });
        var guessed = new Uri(SocketAddress(socket).GetLeftPart(UriPartial.Authority) + "/devtools/0000");

        Assert.Throws<AggregateException>(() => Connect(socket, guessed, "devtools://devtools"));
    }

    [Test]
    public void AnotherOrigin_IsTurnedAway()
    {
        using var socket = new WebViewDevToolsSocket(_ => { }, () => { });

        Assert.Throws<AggregateException>(() => Connect(socket, SocketAddress(socket), "https://example.org"));
    }

    private static Uri SocketAddress(WebViewDevToolsSocket socket)
    {
        var address = socket.FrontEndAddress;
        return new Uri("ws://" + address[(address.IndexOf("ws=", StringComparison.Ordinal) + 3)..]);
    }

    private static ClientWebSocket Connect(WebViewDevToolsSocket socket, Uri address, string origin)
    {
        var client = new ClientWebSocket();
        client.Options.SetRequestHeader("Origin", origin);
        client.ConnectAsync(address, CancellationToken.None).Wait(5000);
        return client;
    }
}
