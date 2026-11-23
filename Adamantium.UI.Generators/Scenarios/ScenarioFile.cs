using System.Collections.Generic;

namespace Adamantium.UI.Generators.Scenarios;

/// <summary>A scenario file read: the test it becomes - its name, description, why it runs only when named, the systems it
/// runs on, and the code of its steps - or what is wrong with it.</summary>
public sealed class ScenarioFile(string path, string name, int nameLine, string description, string explicitReason,
    List<string> steps, List<ScenarioProblem> problems, IReadOnlyList<string> platforms = null, string arguments = null,
    bool usesSystemInput = false, bool asksToStartVisible = false)
{
    /// <summary>Whether the scenario asks to start on the desktop someone sees (<c>Visible</c>) - for what the system
    /// gives only there; as with <see cref="UsesSystemInput"/>, only a test run by name starts there.</summary>
    public bool AsksToStartVisible { get; } = asksToStartVisible;

    /// <summary>Whether the application starts on the desktop someone sees: a test run by name that needs it - by a
    /// step through the system's pointer, or by asking.</summary>
    public bool StartsVisible => ExplicitReason != null && (UsesSystemInput || AsksToStartVisible);

    /// <summary>Whether a step goes through the system's own pointer (<c>ThroughSystem</c>): that input reaches only the
    /// desktop someone sees, so a test run by name (<see cref="ExplicitReason"/>) starts the application there rather
    /// than where nobody sees it.</summary>
    public bool UsesSystemInput { get; } = usesSystemInput;

    /// <summary>The command line the application is started with; null for none.</summary>
    public string Arguments { get; } = arguments;

    /// <summary>The systems the test runs on, as the scenario names them; empty for every system.</summary>
    public IReadOnlyList<string> Platforms { get; } = platforms ?? [];

    public string Path { get; } = path;

    public string Name { get; } = name;

    public int NameLine { get; } = nameLine;

    public string Description { get; } = description;

    /// <summary>Why the test runs only when asked for by name; null for a test that runs with the rest.</summary>
    public string ExplicitReason { get; } = explicitReason;

    public List<string> Steps { get; } = steps;

    public List<ScenarioProblem> Problems { get; } = problems;
}
