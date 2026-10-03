using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Adamantium.UI.LanguageServer;

/// <summary>Gives every file the project's <see cref="BuildProperties"/>.</summary>
public sealed class BuildPropertiesProvider : AnalyzerConfigOptionsProvider
{
    private readonly BuildProperties _properties;

    public BuildPropertiesProvider(BuildProperties properties)
    {
        _properties = properties;
    }

    public override AnalyzerConfigOptions GlobalOptions => _properties;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _properties;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _properties;
}
