using System;
using System.IO;
using System.Linq;
using Adamantium.UI.Generators.WebHost;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// What a page cannot call is an error of the build, said at the member; what it can is made into the object it calls
// through and an AddHostObject for it. The WebView library's real use of what is made is in its own tests.
[TestFixture]
public class WebHostObjectGeneratorTests
{
    private const string Attribute = @"
namespace Adamantium.UI.WebView
{
    [System.AttributeUsage(System.AttributeTargets.Interface)]
    public sealed class WebHostObjectAttribute : System.Attribute {}
    public interface IWebScriptFunction {}
}";

    [Test]
    public void AnInterfaceAPageCanCall_IsMade_EachMethodUnderItsJavaScriptName()
    {
        var (generated, diagnostics) = Run(@"
using System.Threading.Tasks;
using Adamantium.UI.WebView;

namespace Probe
{
    [WebHostObject]
    public interface ICalculator : IBase
    {
        int Add(int a, int b);
        Task SaveAsync(string text);
        ValueTask<ICounter> CounterAsync();
        void CallBack(IWebScriptFunction function);
    }

    public interface IBase { string Name(); }

    [WebHostObject]
    internal interface ICounter { int Increment(); }
}");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(generated, Does.Contain("public sealed class CalculatorHostObject")
                .And.Contain("internal sealed class CounterHostObject")
                .And.Contain("public static class CalculatorHostObjectBridge")
                .And.Contain("case \"add\":").And.Contain("case \"saveAsync\":").And.Contain("case \"counterAsync\":")
                .And.Contain("case \"name\":"), "the methods of the interfaces it extends too");
            Assert.That(generated, Does.Contain("new global::Probe.CounterHostObject(given)"),
                "an interface a page calls is answered as an object of its own");
            Assert.That(generated, Does.Contain("arguments.Function(0)"), "a function of the page's");
        });
    }

    [TestCase("int Value { get; }", "AWV001")]
    [TestCase("event System.EventHandler Changed;", "AWV001")]
    [TestCase("int Add(int a); int add(int a, int b);", "AWV002")]
    [TestCase("void Take(ref int a);", "AWV003")]
    [TestCase("void Take(out int a);", "AWV003")]
    [TestCase("void Take(System.Threading.CancellationToken token);", "AWV003")]
    [TestCase("T Take<T>(T a);", "AWV004")]
    public void WhatAPageCannotCall_IsAnErrorOfTheBuild(string member, string id)
    {
        var (generated, diagnostics) = Run(@"
namespace Probe
{
    [Adamantium.UI.WebView.WebHostObject]
    public interface IProbe { " + member + @" }
}");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(d => d.Id), Does.Contain(id));
            Assert.That(diagnostics.All(d => d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource), Is.True);
            Assert.That(generated, Is.Empty, "nothing half made");
        });
    }

    [Test]
    public void AnInterfaceInAType_IsAnErrorOfTheBuild()
    {
        var (_, diagnostics) = Run(@"
namespace Probe
{
    public class Outer
    {
        [Adamantium.UI.WebView.WebHostObject]
        public interface IProbe { int Add(int a, int b); }
    }
}");

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "AWV005" }));
    }

    private static (string Generated, Diagnostic[] Diagnostics) Run(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
        var compilation = CSharpCompilation.Create("WebHostProbe",
            [CSharpSyntaxTree.ParseText(Attribute), CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new WebHostObjectGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        var generated = string.Join("\n", driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(file => file.SourceText.ToString()));
        return (generated, diagnostics.ToArray());
    }
}
