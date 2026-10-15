using System;
using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adamantium.UI.Core.Data;

// Fans a shared source out to its bindings through one PropertyChanged subscription; subscribers sit in a weak set, so
// add/remove is O(1) instead of rebuilding a multicast list. The subscription goes with the last binding that leaves.
internal static class SharedSourceRegistry
{
    // source (WEAK key -> the registry never pins a source) -> its single fan-out entry.
    private static readonly ConditionalWeakTable<INotifyPropertyChanged, SourceEntry> _bySource = new();

    private static readonly object _tableGate = new();

    public static void Subscribe(INotifyPropertyChanged source, BindingExpression binding)
    {
        lock (_tableGate)
        {
            _bySource.GetValue(source, static s => new SourceEntry(s)).Add(binding);
        }
    }

    public static void Unsubscribe(INotifyPropertyChanged source, BindingExpression binding)
    {
        lock (_tableGate)
        {
            if (!_bySource.TryGetValue(source, out var entry) || !entry.Remove(binding))
            {
                return;
            }

            entry.Detach();
            _bySource.Remove(source);
        }
    }

    private sealed class SourceEntry
    {
        private static readonly object Present = new();
        // Weak SET of subscribers: O(1) add/remove, and a subscriber whose target was GC'd is dropped automatically.
        private readonly ConditionalWeakTable<BindingExpression, object> _subscribers = new();
        private readonly object _gate = new();
        private readonly INotifyPropertyChanged _source;
        private int _count;

        public SourceEntry(INotifyPropertyChanged source)
        {
            _source = source;
            source.PropertyChanged += OnSourceChanged;
        }

        public void Add(BindingExpression b)
        {
            lock (_gate)
            {
                if (_subscribers.TryAdd(b, Present))
                {
                    _count++;
                }
            }
        }

        public bool Remove(BindingExpression b)
        {
            lock (_gate)
            {
                if (_subscribers.Remove(b))
                {
                    _count--;
                }

                return _count == 0;
            }
        }

        public void Detach() => _source.PropertyChanged -= OnSourceChanged;

        // Snapshot subscribers under the gate, then call out outside it, since a producer binding may re-enter
        // Subscribe/Unsubscribe. A snapshot of its OWN per fire: a subscriber can make the source fire again before this
        // one is through, and a shared one would be emptied under it.
        private void OnSourceChanged(object sender, PropertyChangedEventArgs e)
        {
            var pool = ArrayPool<BindingExpression>.Shared;
            BindingExpression[] fire;
            var n = 0;

            lock (_gate)
            {
                fire = pool.Rent(Math.Max(_count, 1));
                foreach (var kv in _subscribers)
                {
                    if (n == fire.Length)
                    {
                        var grown = pool.Rent(n * 2);
                        Array.Copy(fire, grown, n);
                        Array.Clear(fire, 0, n);
                        pool.Return(fire);
                        fire = grown;
                    }

                    fire[n++] = kv.Key;
                }
            }

            try
            {
                for (var i = 0; i < n; i++)
                {
                    fire[i].OnSourcePropertyChanged(sender, e);
                }
            }
            finally
            {
                Array.Clear(fire, 0, n);
                pool.Return(fire);
            }
        }
    }
}
