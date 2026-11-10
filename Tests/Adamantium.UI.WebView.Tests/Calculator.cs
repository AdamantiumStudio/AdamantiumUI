using System;
using System.Threading.Tasks;

namespace Adamantium.UI.WebView.Tests;

/// <summary>The tests' <see cref="ICalculator"/>; its other public members are no page's.</summary>
public sealed class Calculator : ICalculator
{
    public int Add(int a, int b) => a + b;

    public async Task<string> GreetAsync(string name)
    {
        await Task.Delay(10);
        return "hello " + name;
    }

    public void Fail() => throw new InvalidOperationException("broken");

    public ICounter Counter() => new Counter();

    public async Task<ICounter> CounterLaterAsync()
    {
        await Task.Delay(300);
        return new Counter();
    }

    public async Task<string> CallBackAsync(IWebScriptFunction function)
    {
        using (function)
        {
            return await function.CallAsync(20, 22);
        }
    }

    public Point Mirror(Point point) => new(point.Y, point.X);

    public string Secret() => "not the page's";
}
