using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.Multiverse;
using Adamantium.Graphics.Core;
using Adamantium.UI.Core;
using Adamantium.UI.EntityServices;

namespace Adamantium.UI.Universes;

public class UniverseService : IUniverseService
{
    private readonly object _locker = new object();

    private readonly List<UniverseKey> _universes;

    private readonly List<IUniverse> _retired = [];

    private readonly EntityWorld _world;

    public UniverseService(EntityWorld world = null)
    {
        _universes = [];
        _world = world;
    }

    public IReadOnlyList<IUniverse> Universes
    {
        get
        {
            lock (_locker)
            {
                return _universes.Select(x=>x.Universe).ToList();
            }
        }
    }

    public T CreateUniverse<T>(string name, IWindow wnd, EntityService service, params object[] args) where T : IUniverse
    {
        var universe = (T)Activator.CreateInstance(typeof(T), args);
        universe.InitializeUniverse();
        var key = new UniverseKey(name, wnd, service, universe);
        lock (_locker)
        {
            _universes.Add(key);
        }

        OnUniverseAdded?.Invoke(universe);
        return universe;
    }

    public async Task<T> CreateUniverseAsync<T>(string name, Func<T> create, CancellationToken cancellationToken = default)
        where T : IUniverse
    {
        var universe = await Task.Run(() =>
        {
            var made = create();
            try
            {
                made.InitializeUniverse();
            }
            catch
            {
                lock (_locker)
                {
                    _retired.Add(made);
                }

                throw;
            }

            return made;
        }, cancellationToken);

        lock (_locker)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _retired.Add(universe);
            }
            else
            {
                _universes.Add(new UniverseKey(name, null, null, universe));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        OnUniverseAdded?.Invoke(universe);
        return universe;
    }

    public void Show(IUniverse universe, IWindow window)
    {
        var service = _world?.ServiceManager.GetServices<UiRenderService>()
            .OfType<WindowRenderService>()
            .FirstOrDefault(x => ReferenceEquals(x.Window, window));

        lock (_locker)
        {
            var index = _universes.FindIndex(x => ReferenceEquals(x.Universe, universe));
            if (index >= 0)
            {
                _universes[index] = _universes[index] with { Window = window, Service = service };
            }
        }
    }

    /// <summary>
    /// Takes the universe out; it is stopped on the next <see cref="RunUniverses"/>, the thread that may wait the device idle.
    /// </summary>
    public bool RemoveUniverse(IUniverse universe)
    {
        lock (_locker)
        {
            var result = _universes.FirstOrDefault(x => x.Universe == universe);
            if (result == null)
            {
                return false;
            }

            _universes.Remove(result);
            _retired.Add(universe);
            return true;
        }
    }

    public void RunUniverses(IRenderService renderService, AppTime time)
    {
        lock (_locker)
        {
            for (int i = 0; i < _retired.Count; i++)
            {
                _retired[i].ShutDown();
            }
            _retired.Clear();

            // Matched by window, not by service: the service drawing a window is replaced when the devices are made
            // anew, the window is not. A universe created without a service is driven by whoever created it.
            var window = (renderService as WindowRenderService)?.Window;
            Parallel.ForEach(_universes, (item) =>
            {
                if (item.Service == null || window == null || !ReferenceEquals(item.Window, window))
                {
                    return;
                }

                item.Universe.RunOnce(time);
            });
            foreach (var key in _universes)
            {
                key.Universe.Submit();
            }
        }
    }

    public void CopyOutput(IGraphicsDevice graphicsDevice)
    {
        foreach (var universe in Universes)
        {
            foreach (var output in universe.Outputs)
            {
                if (!output.IsVisible)
                {
                    continue;
                }

                output.CopyOutput(graphicsDevice);
            }
        }
    }

    public event Action<IUniverse> OnUniverseAdded;

    private sealed record UniverseKey(string Name, IWindow Window, EntityService Service, IUniverse Universe);
}
