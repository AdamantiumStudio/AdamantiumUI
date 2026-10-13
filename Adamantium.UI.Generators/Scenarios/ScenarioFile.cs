using System.Collections.Generic;

namespace Adamantium.UI.Generators.Scenarios;

/// <summary>A scenario file read: the test it becomes - its name, description, why it runs only when named, and the code
/// of its steps - or what is wrong with it.</summary>
public sealed class ScenarioFile(string path, string name, int nameLine, string description, string explicitReason,
    List<string> steps, List<ScenarioProblem> problems)
{
    public string Path { get; } = path;

    public string Name { get; } = name;

    public int NameLine { get; } = nameLine;

    public string Description { get; } = description;

    /// <summary>Why the test runs only when asked for by name; null for a test that runs with the rest.</summary>
    public string ExplicitReason { get; } = explicitReason;

    public List<string> Steps { get; } = steps;

    public List<ScenarioProblem> Problems { get; } = problems;
}
