using System;

namespace Adamantium.UI.Core.Data;

/// <summary>The thread a binding's source lives on - a game world's, say - for the bindings that write it there rather
/// than on the UI's: those that say <see cref="Binding.WorkOnSourceThread"/>, under an element whose
/// <see cref="BindingEngine.SourceThreadProperty"/> names this, bound to an object it <see cref="Holds"/>.</summary>
public interface ISourceThread
{
    /// <summary>Whether <paramref name="source"/> lives on this thread. One that does not - a view model beside the
    /// world's objects in the same panel - is written at once, on the UI's thread.</summary>
    bool Holds(object source);

    /// <summary>Runs <paramref name="update"/> on the source's thread, later, in the order the updates were given. Called
    /// on the UI's thread.</summary>
    void Run(Action update);
}
