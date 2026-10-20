using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.Multiverse;
using Adamantium.Graphics.Core;
using Adamantium.UI.Core;

namespace Adamantium.UI.Universes;

public interface IUniverseService
{
    public IReadOnlyList<IUniverse> Universes { get; }

    public T CreateUniverse<T>(string name, IWindow wnd, EntityService service, params object[] args) where T : IUniverse;

    /// <summary>Makes a universe with <paramref name="create"/> and initializes it off the UI thread. It does not run
    /// until it is shown in a window (<see cref="Show"/>). A universe whose making is canceled is shut down.</summary>
    public Task<T> CreateUniverseAsync<T>(string name, Func<T> create, CancellationToken cancellationToken = default)
        where T : IUniverse;

    /// <summary>Shows <paramref name="universe"/> in <paramref name="window"/> and starts its own loop: it runs on its own
    /// thread, at the pace its frames are shown, not in the window's frames. A universe shown in another window before
    /// moves here.</summary>
    public void Show(IUniverse universe, IWindow window);

    public bool RemoveUniverse(IUniverse universe);

    /// <summary>Shuts down the universes removed since the last call, waiting for their loops to end.</summary>
    public void RetireRemoved();

    /// <summary>Holds every universe's own loop between two frames - for remaking the devices they draw with - until
    /// <see cref="ResumeLoops"/>.</summary>
    public void SuspendLoops();

    /// <summary>Lets the loops held by <see cref="SuspendLoops"/> go on.</summary>
    public void ResumeLoops();

    public void CopyOutput(IGraphicsDevice graphicsDevice);

    public event Action<IUniverse> OnUniverseAdded;
}
