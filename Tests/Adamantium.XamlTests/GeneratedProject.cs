using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Adamantium.XamlTests;

/// <summary>A project the codegen harness generated and compiled: what the generators said, and the assembly to load.</summary>
internal sealed class GeneratedProject
{
    private static readonly Dictionary<string, Assembly> Loaded = new();
    private byte[] _image;
    private Assembly _assembly;

    static GeneratedProject()
    {
        // Each image loads into a context of its own, so one generated assembly referencing another finds it here.
        System.AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            lock (Loaded)
            {
                return Loaded.TryGetValue(new AssemblyName(args.Name).Name ?? string.Empty, out var assembly) ? assembly : null;
            }
        };
    }

    public GeneratedProject(CSharpCompilation compilation, IReadOnlyList<Diagnostic> generatorDiagnostics)
    {
        Compilation = compilation;
        GeneratorDiagnostics = generatorDiagnostics;
    }

    public CSharpCompilation Compilation { get; }

    public IReadOnlyList<Diagnostic> GeneratorDiagnostics { get; }

    public IReadOnlyList<Diagnostic> Errors =>
        GeneratorDiagnostics.Concat(Compilation.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    public IReadOnlyList<Diagnostic> Warnings =>
        GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();

    public string Source => string.Join("\n\n", Compilation.SyntaxTrees.Select(t => t.GetText().ToString()));

    public MetadataReference Reference => MetadataReference.CreateFromImage(Image());

    public Assembly Load()
    {
        if (_assembly != null)
        {
            return _assembly;
        }

        _assembly = Assembly.Load(Image());
        lock (Loaded)
        {
            Loaded[Compilation.AssemblyName ?? string.Empty] = _assembly;
        }

        return _assembly;
    }

    private byte[] Image()
    {
        if (_image != null)
        {
            return _image;
        }

        using var stream = new MemoryStream();
        var result = Compilation.Emit(stream);
        if (!result.Success)
        {
            throw new System.InvalidOperationException(
                "the generated project does not compile: " + string.Join(" | ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        _image = stream.ToArray();
        return _image;
    }
}
