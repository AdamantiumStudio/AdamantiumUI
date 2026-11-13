using System;
using System.Collections.Concurrent;
using System.Threading;
using Adamantium.UI.Core.Templates;

namespace Adamantium.XamlTests;

/// <summary>Builds kept pages for a content template, holding the first few until a test lets each go on.</summary>
internal sealed class Builds : IDisposable
{
    private readonly ManualResetEventSlim[] _started;
    private readonly ManualResetEventSlim[] _gates;
    private readonly ConcurrentQueue<KeptPage> _pages = new();
    private int _count;

    public Builds(int gated)
    {
        _started = new ManualResetEventSlim[gated];
        _gates = new ManualResetEventSlim[gated];
        for (var i = 0; i < gated; i++)
        {
            _started[i] = new ManualResetEventSlim(false);
            _gates[i] = new ManualResetEventSlim(false);
        }
    }

    /// <summary>How many builds have started.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>The pages built, in the order the builds started.</summary>
    public KeptPage[] Pages => _pages.ToArray();

    public TemplateResult Build()
    {
        var number = Interlocked.Increment(ref _count);
        var page = new KeptPage();
        _pages.Enqueue(page);
        if (number <= _gates.Length)
        {
            _started[number - 1].Set();
            _gates[number - 1].Wait(TimeSpan.FromSeconds(10));
        }

        return new TemplateResult { RootComponent = page };
    }

    /// <summary>Waits until build <paramref name="number"/> (from 1) has started.</summary>
    public bool Started(int number) => _started[number - 1].Wait(TimeSpan.FromSeconds(5));

    /// <summary>Lets build <paramref name="number"/> (from 1) finish.</summary>
    public void Open(int number) => _gates[number - 1].Set();

    public void Dispose()
    {
        foreach (var gate in _gates)
        {
            gate.Set();
        }

        foreach (var gate in _gates)
        {
            gate.Dispose();
        }

        foreach (var started in _started)
        {
            started.Dispose();
        }
    }
}
