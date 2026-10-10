using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering.Verification;

internal static class ComponentText
{
    public static string Of(IUIComponent component)
    {
        if (component == null)
        {
            return "(none)";
        }

        var name = component is Controls.Base.UIComponent { Name: { Length: > 0 } named } ? $" '{named}'" : string.Empty;
        return $"{component.GetType().Name}{name} #{Short(component.RenderId.ToString())}";
    }

    public static string WithWorld(IUIComponent component)
    {
        if (component == null)
        {
            return "(none)";
        }

        var size = component.RenderSize;
        var world = new Rect(0, 0, size.Width, size.Height).TransformToAABB(component.WorldTransform);
        return $"{Of(component)} {Box(world)} vis={component.Visibility}";
    }

    public static string Box(Rect rect) => $"[{rect.X:0.#},{rect.Y:0.#} {rect.Width:0.#}x{rect.Height:0.#}]";

    private static string Short(string id) => id.Length > 8 ? id[..8] : id;
}
