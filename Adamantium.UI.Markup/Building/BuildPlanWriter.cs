namespace Adamantium.UI.Markup.Building;

internal sealed class BuildPlanWriter
{
    private readonly Stack<BuildScope> _scopes = new();

    public BuildPlanWriter(BuildScope root)
    {
        _scopes.Push(root);
    }

    public void Add(BuildStep step)
    {
        _scopes.Peek().Steps.Add(step);
    }

    public void BlankLine()
    {
        Add(new BlankLineStep());
    }

    public void Open(BuildScope scope)
    {
        Add(scope);
        _scopes.Push(scope);
    }

    public void Close()
    {
        _scopes.Pop();
    }
}
