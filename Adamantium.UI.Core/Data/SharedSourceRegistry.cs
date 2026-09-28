using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adamantium.UI.Core.Data;

// Fans a shared source out to its bindings through one PropertyChanged subscription; subscribers sit in a weak set, so
// add/remove is O(1) instead of rebuilding a multicast list.
internal static class SharedSourceRegistry
{
    // source (WEAK key -> the registry never pins a source) -> its single fan-out entry.
    private static readonly ConditionalWeakTable<INotifyPropertyChanged, SourceEntry> _bySource = new();

    public static void Subscribe(INotifyPropertyChanged source, BindingExpression binding)
        => _bySource.GetValue(source, static s => new SourceEntry(s)).Add(binding);

    public static void Unsubscribe(INotifyPropertyChanged source, BindingExpression binding)
    {
        if (_bySource.TryGetValue(source, out var entry)) entry.Remove(binding);
    }

    private sealed class SourceEntry
    {
        private static readonly object Present = new();
        // Weak SET of subscribers: O(1) add/remove, and a subscriber whose target was GC'd is dropped automatically.
        private readonly ConditionalWeakTable<BindingExpression, object> _subscribers = new();
        private readonly object _gate = new();
        private BindingExpression[] _fireBuf = new BindingExpression[16];   // reused fire snapshot (no per-fire alloc)

        public SourceEntry(INotifyPropertyChanged source) => source.PropertyChanged += OnSourceChanged;

        public void Add(BindingExpression b) { lock (_gate) _subscribers.AddOrUpdate(b, Present); }
        public void Remove(BindingExpression b) { lock (_gate) _subscribers.Remove(b); }

        // Snapshot subscribers under the gate, then call out outside it, since a producer binding may re-enter
        // Subscribe/Unsubscribe.
        private void OnSourceChanged(object sender, PropertyChangedEventArgs e)
        {
            int n = 0;
            lock (_gate)
            {
                foreach (var kv in _subscribers)
                {
                    if (n == _fireBuf.Length) Array.Resize(ref _fireBuf, n * 2);
                    _fireBuf[n++] = kv.Key;
                }
            }
            for (var i = 0; i < n; i++)
            {
                _fireBuf[i].OnSourcePropertyChanged(sender, e);
                _fireBuf[i] = null;   // don't pin subscribers between fires
            }
        }
    }
}
