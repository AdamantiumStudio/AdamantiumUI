using System;
using System.Xml.Linq;

namespace Adamantium.UI.Generators.Scenarios;

internal sealed class ScenarioStepException(string id, string message, XObject node) : Exception(message)
{
    public string Id { get; } = id;

    public XObject Node { get; } = node;
}
