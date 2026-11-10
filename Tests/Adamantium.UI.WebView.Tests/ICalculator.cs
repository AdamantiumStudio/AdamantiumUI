using System.Threading.Tasks;

namespace Adamantium.UI.WebView.Tests;

/// <summary>What the tests' pages call: a value at once, an awaited one, a failure, another object, a function of the
/// page's called back, and a value of several fields.</summary>
[WebHostObject]
public interface ICalculator
{
    int Add(int a, int b);

    Task<string> GreetAsync(string name);

    void Fail();

    ICounter Counter();

    Task<ICounter> CounterLaterAsync();

    Task<string> CallBackAsync(IWebScriptFunction function);

    Point Mirror(Point point);
}
