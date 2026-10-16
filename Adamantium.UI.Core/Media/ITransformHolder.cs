namespace Adamantium.UI.Core.Media;

internal interface ITransformHolder
{
    bool RedrawsItself { get; }

    void OnTransformChanged(Transform transform);

    AdamantiumComponent ParentFor(Transform transform);
}
