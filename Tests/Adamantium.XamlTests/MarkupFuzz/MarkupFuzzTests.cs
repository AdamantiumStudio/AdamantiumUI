using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.LanguageServer;
using Adamantium.UI.Markup.CodeGeneration;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Adamantium.XamlTests.MarkupFuzz;

/// <summary>Every markup file of the repository, spoiled one place at a time the way an author spoils it mid-edit. Each
/// mistake must come back as a message: the generator, the live preview and the language server never crash on it,
/// and a value of a checked type that names nothing never passes the build or the preview in silence.</summary>
[TestFixture]
[Explicit("Sweeps every markup file of the repository: minutes, not seconds.")]
[Category("MarkupFuzz")]
public class MarkupFuzzTests
{
    private const int MutationsPerFile = 12;

    [Test]
    public void EveryMistake_IsReported_NeverACrashOrASilence()
    {
        var root = RepositoryRoot();
        var model = BuildModel();
        var findings = new List<string>();
        var report = Path.Combine(root, "artifacts", "markup-fuzz-report.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report, string.Empty);
        foreach (var file in MarkupFiles(root))
        {
            var before = findings.Count;
            var text = File.ReadAllText(file);
            var relative = ProjectRelative(file);
            var baselineBuild = Errors(AumlCodegenHarness.Diagnose(relative, text, compile: false));
            var baselinePreview = Crashes(Preview(text, out _));
            foreach (var mutation in MarkupMutations.Of(text, MutationsPerFile))
            {
                Check(Path.GetRelativePath(root, file), relative, mutation, baselineBuild.Count == 0, baselinePreview, model, findings);
            }

            File.AppendAllLines(report, findings.Skip(before));
        }

        Assert.That(findings, Is.Empty, $"{findings.Count} findings, listed in {report}");
    }

    private static void Check(string file, string relative, MarkupMutation mutation, bool buildsAlone,
        IReadOnlySet<string> baselinePreview, AumlTypeModel model, List<string> findings)
    {
        var where = $"{file} @{mutation.Offset} [{mutation.Kind}] {mutation.Element}.{mutation.Attribute}";

        IReadOnlyList<Diagnostic> build;
        try
        {
            build = AumlCodegenHarness.Diagnose(relative, mutation.Text, compile: false);
        }
        catch (Exception e)
        {
            findings.Add($"{where}: the generator threw {FirstLine(e.ToString())}");
            return;
        }

        foreach (var crash in build.Where(d => d.Id == "AUI900"))
        {
            findings.Add($"{where}: the generator crashed: {FirstLine(crash.GetMessage())}");
        }

        List<string> preview;
        try
        {
            preview = Preview(mutation.Text, out _);
        }
        catch (Exception e)
        {
            findings.Add($"{where}: the preview threw {FirstLine(e.ToString())}");
            preview = [];
        }

        foreach (var crash in Crashes(preview).Except(baselinePreview))
        {
            findings.Add($"{where}: the preview crashed: {FirstLine(crash)}");
        }

        foreach (var (step, action) in LanguageServerSteps(mutation, model, relative))
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                findings.Add($"{where}: the language server's {step} threw {FirstLine(e.ToString())}");
            }
        }

        if (mutation.Kind != "value naming nothing" || !buildsAlone || !IsChecked(mutation, model))
        {
            return;
        }

        var compiled = Errors(AumlCodegenHarness.Diagnose(relative, mutation.Text, compile: true));
        if (!compiled.Any(e => e.Contains(MarkupMutations.Bogus, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add($"{where}: the build took a value naming nothing");
        }

        if (!preview.Any(d => d.Contains(MarkupMutations.Bogus, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add($"{where}: the preview took a value naming nothing");
        }
    }

    private static IEnumerable<(string Step, Action Action)> LanguageServerSteps(MarkupMutation mutation, AumlTypeModel model, string relative)
    {
        var text = mutation.Text;
        var caret = Math.Min(text.Length, mutation.Offset + 1);
        yield return ("validation", () => AumlValidator.Validate(text, model));
        yield return ("coloring", () => SemanticTokensEngine.Tokenize(text, model));
        yield return ("formatting", () => AumlFormatter.Format(text, new AumlFormatOptions()));
        yield return ("completion", () => new CompletionEngine(model).Complete(text, caret, relative));
    }

    private static bool IsChecked(MarkupMutation mutation, AumlTypeModel model)
    {
        if (mutation.Element == null || mutation.Attribute == null || mutation.Attribute.Contains('.') || mutation.Attribute.Contains(':'))
        {
            return false;
        }

        var namespaces = AumlNamespaces.Scan(mutation.Text);
        var colon = mutation.Element.IndexOf(':');
        if (!namespaces.TryGetValue(colon < 0 ? string.Empty : mutation.Element[..colon], out var xmlns))
        {
            return false;
        }

        var element = model.GetElement(xmlns, mutation.Element[(colon + 1)..]);
        var type = element == null ? null : model.GetPropertyType(element, mutation.Attribute);
        return type != null && (type.TypeKind == ResolvedTypeKind.Enum || type.FullName == "System.Type"
                                || type.SpecialType is ResolvedSpecialType.System_Boolean or ResolvedSpecialType.System_Double
                                    or ResolvedSpecialType.System_Single or ResolvedSpecialType.System_Int32
                                    or ResolvedSpecialType.System_Int64 or ResolvedSpecialType.System_UInt32);
    }

    private static List<string> Preview(string text, out object root)
    {
        var result = AumlLoader.Load(text);
        root = result.Root;
        return result.Diagnostics.ToList();
    }

    private static HashSet<string> Crashes(IEnumerable<string> diagnostics) =>
        diagnostics.Where(d => d.StartsWith("Resolve error:") || d.StartsWith("Instantiate error:")).ToHashSet();

    private static List<string> Errors(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.GetMessage()).ToList();

    private static string FirstLine(string text) => text.Split('\n')[0].Trim();

    private static AumlTypeModel BuildModel()
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            byName[Path.GetFileName(dll)] = dll;
        }

        foreach (var dll in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            byName.TryAdd(Path.GetFileName(dll), dll);
        }

        return AumlTypeModel.Build(byName.Values);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AdamantiumUI.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (AdamantiumUI.sln) is not above the test output.");
    }

    private static IEnumerable<string> MarkupFiles(string root) =>
        Directory.EnumerateFiles(root, "*.auml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}")
                        && !f.Contains("Adamantium.UI.Templates"))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    private static string ProjectRelative(string file)
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(file)); dir != null; dir = dir.Parent)
        {
            if (dir.GetFiles("*.csproj").Length > 0)
            {
                return Path.GetRelativePath(dir.FullName, file).Replace('\\', '/');
            }
        }

        return Path.GetFileName(file);
    }
}
