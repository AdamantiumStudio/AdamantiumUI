namespace Adamantium.UI.Core.Templates;

/// <summary>Templates picked by the type of the item they draw - a selector written in markup instead of a class. Each
/// <see cref="DataTemplate"/> states its type with <c>x:DataType</c>; the item's own type wins, then its base classes,
/// nearest first, then the first template written for an interface it implements. A template with no type takes what
/// no other fits. Set wherever a template selector goes - inline, or from resources by key.</summary>
public class DataTemplateSet : DataTemplateSelector
{
    private readonly Dictionary<Type, DataTemplate> _chosen = new();
    private int _chosenFrom;

    /// <summary>The templates to pick from, in the order written.</summary>
    [Content]
    public List<DataTemplate> Templates { get; } = [];

    public override DataTemplate SelectTemplate(object item, AdamantiumComponent container)
    {
        if (_chosenFrom != Templates.Count)
        {
            _chosen.Clear();
            _chosenFrom = Templates.Count;
        }

        if (item == null)
        {
            return Fallback();
        }

        var type = item.GetType();
        if (!_chosen.TryGetValue(type, out var chosen))
        {
            chosen = Choose(type);
            _chosen[type] = chosen;
        }

        return chosen;
    }

    private DataTemplate Choose(Type type)
    {
        for (var candidate = type; candidate != null; candidate = candidate.BaseType)
        {
            if (Templates.FirstOrDefault(t => t.DataType == candidate) is { } exact)
            {
                return exact;
            }
        }

        return Templates.FirstOrDefault(t => t.DataType is { IsInterface: true } contract && contract.IsAssignableFrom(type))
               ?? Fallback();
    }

    private DataTemplate Fallback() => Templates.FirstOrDefault(t => t.DataType == null);
}
