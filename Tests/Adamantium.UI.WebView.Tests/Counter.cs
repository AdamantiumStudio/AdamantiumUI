namespace Adamantium.UI.WebView.Tests;

/// <summary>The tests' <see cref="ICounter"/>.</summary>
public sealed class Counter : ICounter
{
    private int count;

    public int Increment() => ++count;
}
