using System;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Adamantium.UI.Generators.Scenarios;

/// <summary>The schema of scenario files, compiled once: what the build checks a scenario against and what editors
/// complete from.</summary>
public static class ScenarioSchema
{
    public static readonly XNamespace Namespace = "http://adamantium/automation/scenario";

    private static readonly Lazy<XmlSchemaSet> Compiled = new(() =>
    {
        var schemas = new XmlSchemaSet();
        using var stream = typeof(ScenarioSchema).Assembly.GetManifestResourceStream("Adamantium.UI.Generators.Scenario.xsd");
        schemas.Add(XmlSchema.Read(stream, null));
        schemas.Compile();
        return schemas;
    });

    public static XmlSchemaSet Set => Compiled.Value;

    /// <summary>The root element, <c>Scenario</c>.</summary>
    public static XmlSchemaElement Root => (XmlSchemaElement)Set.GlobalElements[new System.Xml.XmlQualifiedName("Scenario", Namespace.NamespaceName)];
}
