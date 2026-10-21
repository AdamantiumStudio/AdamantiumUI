using System;
using System.IO;
using System.Linq;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.EventGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// The calls are written into the application's own compilation: what compiles without the generator must compile with
// it, and a delegate the generated code cannot name is left to DynamicInvoke.
[TestFixture]
public class RoutedEventInvokerGeneratorTests
{
    [Test]
    public void WhatTheGeneratedCodeCannotName_IsLeftOut_AndTheRestIsCalledDirectly()
    {
        var (generated, diagnostics) = Run(@"
using Adamantium.UI.Core.RoutedEvents;

namespace Probe
{
    public class GenArgs<T> : RoutedEventArgs {}
    public class PlainArgs : RoutedEventArgs {}

    public class Outer<T>
    {
        public delegate void NestedHandler(object sender, RoutedEventArgs e);
        public static System.Type Named = typeof(System.EventHandler<GenArgs<T>>);
    }

    [System.Obsolete(""old"")] public delegate void OldHandler(object sender, RoutedEventArgs e);
    [System.Obsolete(""gone"", true)] public delegate void GoneHandler(object sender, RoutedEventArgs e);
    [System.Diagnostics.CodeAnalysis.Experimental(""PROBE1"")] public delegate void TrialHandler(object sender, RoutedEventArgs e);
    public delegate void DynamicHandler(dynamic sender, RoutedEventArgs e);

    public class Host
    {
        protected internal delegate void InnerHandler(object sender, PlainArgs e);
        public static System.Type Qualified = typeof(System.EventHandler<PlainArgs>);
        public static System.Type Global = typeof(global::System.EventHandler<GenArgs<int>>);
    }
}");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(generated, Does.Contain("Probe.OldHandler").And.Contain("Probe.Host.InnerHandler")
                .And.Contain("System.EventHandler<global::Probe.PlainArgs>")
                .And.Contain("System.EventHandler<global::Probe.GenArgs<int>>"));
            Assert.That(generated, Does.Not.Contain("NestedHandler").And.Not.Contain("GenArgs<T>")
                .And.Not.Contain("GoneHandler").And.Not.Contain("TrialHandler").And.Not.Contain("DynamicHandler"));
        });
    }

    private static (string Generated, Diagnostic[] Diagnostics) Run(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Append(typeof(RoutedEventArgs).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();
        var compilation = CSharpCompilation.Create("RoutedEventProbe",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new RoutedEventInvokerGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);

        var generated = string.Join("\n", driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString()));
        var diagnostics = generatorDiagnostics
            .Concat(updated.GetDiagnostics().Where(d => d.Location.SourceTree?.FilePath.EndsWith(".g.cs") == true))
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToArray();

        return (generated, diagnostics);
    }
}
