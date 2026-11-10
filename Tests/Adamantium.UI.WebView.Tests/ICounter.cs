namespace Adamantium.UI.WebView.Tests;

/// <summary>An object a call answers with: the page calls it in turn.</summary>
[WebHostObject]
public interface ICounter
{
    int Increment();
}
