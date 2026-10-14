namespace Adamantium.UI.Core.Markup;

internal sealed class BuildFrame(BuildFrame parent)
{
    private readonly BuildFrame _parent = parent;
    private readonly Dictionary<string, object> _variables = new(StringComparer.Ordinal);

    public bool HasReturned { get; private set; }

    public object ReturnValue { get; private set; }

    public void Set(string name, object value)
    {
        _variables[name] = value;
    }

    public bool TryGet(string name, out object value)
    {
        for (var frame = this; frame != null; frame = frame._parent)
        {
            if (frame._variables.TryGetValue(name, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    public void Return(object value)
    {
        ReturnValue = value;
        HasReturned = true;
    }
}
