namespace Adamantium.UI.Core.Media;

internal interface ITransformHolder
{
    void OnTransformChanged(Transform transform);

    AdamantiumComponent ParentFor(Transform transform);
}
