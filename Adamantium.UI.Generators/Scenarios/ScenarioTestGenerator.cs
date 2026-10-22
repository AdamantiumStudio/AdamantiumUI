using System;
using Microsoft.CodeAnalysis;

namespace Adamantium.UI.Generators.Scenarios;

/// <summary>Turns scenario files (<c>*.ascenario</c>) into NUnit tests on the automation library, for the application
/// the project names in <c>AdamantiumScenarioApplication</c>, laid out in the font <c>AdamantiumScenarioFont</c> names
/// when it names one.</summary>
[Generator]
public sealed class ScenarioTestGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var settings = context.AnalyzerConfigOptionsProvider.Select((options, _) =>
        {
            options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);
            options.GlobalOptions.TryGetValue("build_property.AdamantiumScenarioApplication", out var application);
            options.GlobalOptions.TryGetValue("build_property.AdamantiumScenarioFont", out var font);
            return (RootNamespace: rootNamespace, Application: application, Font: font);
        });

        var files = context.AdditionalTextsProvider
            .Where(text => text.Path.EndsWith(ScenarioTranslator.Extension, StringComparison.OrdinalIgnoreCase))
            .Select((text, cancellationToken) => ScenarioTranslator.Read(text.Path, text.GetText(cancellationToken)?.ToString() ?? string.Empty))
            .Collect();

        var testable = context.CompilationProvider.Select((compilation, _) =>
            compilation.GetTypeByMetadataName("NUnit.Framework.TestAttribute") != null &&
            compilation.GetTypeByMetadataName("Adamantium.UI.Automation.AutomationSession") != null);

        context.RegisterSourceOutput(files.Combine(settings).Combine(testable),
            (output, source) => ScenarioTestEmitter.Emit(output, source.Left.Left, source.Left.Right.RootNamespace,
                source.Left.Right.Application, source.Right, source.Left.Right.Font));
    }
}
