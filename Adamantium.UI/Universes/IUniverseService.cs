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

    /// <summary>Makes a universe with <paramref name="create"/> and initializes it off the UI thread. It runs in no
    /// window's frames until it is shown in one (<see cref="Show"/>). A universe whose making is canceled is shut down.</summary>
    public Task<T> CreateUniverseAsync<T>(string name, Func<T> create, CancellationToken cancellationToken = default)
        where T : IUniverse;

    /// <summary>Runs <paramref name="universe"/> in the frames of <paramref name="window"/>; a universe shown in another
    /// window before moves here.</summary>
    public void Show(IUniverse universe, IWindow window);

    public bool RemoveUniverse(IUniverse universe);

    public void RunUniverses(IRenderService renderService, AppTime time);

    public void CopyOutput(IGraphicsDevice graphicsDevice);

    public event Action<IUniverse> OnUniverseAdded;
}
