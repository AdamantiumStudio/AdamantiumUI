namespace Adamantium.UI.Core.Resources;

// A logical-only part that answers its deferred resources itself, from where it stands, when its theme scope changes.
internal interface IResolvesOwnResources
{
    void ResolveResources();
}
