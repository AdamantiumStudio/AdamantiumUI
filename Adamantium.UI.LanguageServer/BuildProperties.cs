using Microsoft.CodeAnalysis.Diagnostics;

namespace Adamantium.UI.LanguageServer;

/// <summary>The MSBuild properties a generator reads, as the build passes them.</summary>
public sealed class BuildProperties : AnalyzerConfigOptions
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public BuildProperties(IReadOnlyDictionary<string, string> values)
    {
        _values = values;
    }

    public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value);
}
