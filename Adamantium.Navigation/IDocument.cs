using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.Navigation;

/// <summary>A pane's view model with work that can be unsaved. Its tab shows when it is; closing it - alone, with
/// others, or with the window - asks once whether to save; saving it calls <see cref="SaveAsync"/>.</summary>
public interface IDocument
{
    /// <summary>Changed and not saved yet. Raise PropertyChanged for it: the tab follows.</summary>
    bool IsDirty { get; }

    /// <summary>Saves; false when it could not, which keeps the pane open.</summary>
    Task<bool> SaveAsync(CancellationToken cancellationToken = default);
}
