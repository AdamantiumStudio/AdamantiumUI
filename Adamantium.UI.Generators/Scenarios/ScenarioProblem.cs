namespace Adamantium.UI.Generators.Scenarios;

/// <summary>What is wrong at one place of a scenario file, under a stable diagnostic id.</summary>
public sealed class ScenarioProblem(string id, string message, int line, int column)
{
    public string Id { get; } = id;

    public string Message { get; } = message;

    public int Line { get; } = line;

    public int Column { get; } = column;
}
