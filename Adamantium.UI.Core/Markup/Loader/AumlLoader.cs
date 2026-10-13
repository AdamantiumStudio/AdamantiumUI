using System.Reflection;
using System.Runtime.CompilerServices;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Markup;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.Building;
using Adamantium.UI.Markup.CodeGeneration;
using Adamantium.UI.Markup.CodeGeneration.Reflection;
using Adamantium.UI.Markup.Parsers;

namespace Adamantium.UI.Core.Markup;

/// <summary>
/// Runtime AUML loader: parses markup, resolves it through the engine's own <c>DefaultAumlTransformer</c>
/// (backed by a <see cref="ReflectionTypeResolver"/> over the real loaded assemblies), lowers it to the build plan the
/// generator compiles, and runs that plan by reflection. Used by the live designer to turn an editor buffer into a
/// renderable tree without compiling.
/// </summary>
public static class AumlLoader
{
    private static readonly ConditionalWeakTable<Assembly, LanguageTableShape[]> TablesByAssembly = new();

    /// <param name="typeMapper">Optional substitution applied to every resolved type before instantiation
    /// (e.g. map a root <c>IWindow</c> to a headless virtual window). Returns the type to instantiate.</param>
    public static AumlLoadResult Load(string aumlText, IEnumerable<Assembly> assemblies = null, Func<Type, Type> typeMapper = null)
    {
        var result = new AumlLoadResult();
        var lowered = Lower(aumlText, assemblies, result.Diagnostics);
        result.Ast = lowered?.Ast;
        result.Plan = lowered?.Plan;
        if (lowered?.Plan == null)
        {
            return result;
        }

        var executor = new BuildPlanExecutor(lowered.Resolver, lowered.Assemblies, typeMapper, result.Diagnostics);
        try
        {
            var root = lowered.EntityType == EntityType.Unknown ? null : CreateRoot(lowered.Ast, lowered.Resolver, typeMapper);
            result.Root = executor.Run(lowered.Plan, root);
        }
        catch (Exception e)
        {
            result.Diagnostics.Add($"Instantiate error: {e.Message}");
        }

        result.SourceMap = executor.SourceMap;
        return result;
    }

    internal static LoweredDocument Lower(string aumlText, IEnumerable<Assembly> assemblies, List<string> diagnostics,
        string sourceFilePath = null)
    {
        AumlDocument doc;
        try { doc = AumlParser.Parse(aumlText); }
        catch (Exception e) { diagnostics.Add($"Parse error: {e.Message}"); return null; }

        if (doc.Root == null) { diagnostics.Add("Empty / invalid AUML"); return null; }
        doc.RelativeFilePath ??= "preview.auml";
        doc.RootNamespace ??= "Adamantium.Designer.Preview";
        doc.SourceFilePath = sourceFilePath;

        var asmList = (assemblies ?? AppDomain.CurrentDomain.GetAssemblies()).ToList();
        var resolver = new ReflectionTypeResolver(asmList);
        try
        {
            var container = Transformer(asmList).Transform(doc, resolver, new ListDiagnosticSink(diagnostics));
            var plan = AumlLowering.Lower(container, new ListDiagnosticSink(diagnostics));
            return new LoweredDocument(doc.Root, container.RootEntityType, plan, resolver, asmList);
        }
        catch (Exception e)
        {
            diagnostics.Add($"Resolve error: {e.Message}");
            return new LoweredDocument(doc.Root, EntityType.Unknown, null, resolver, asmList);
        }
    }

    /// <summary>Applies edited markup to <paramref name="liveRoot"/>, built from <paramref name="previous"/>, in place.
    /// <see cref="AumlLoadResult.Reconciled"/> is false when the root type changed or the markup does not build, and the
    /// caller must rebuild.</summary>
    public static AumlLoadResult Reconcile(object liveRoot, AumlLoadResult previous, string newAumlText,
        IEnumerable<Assembly> assemblies = null, Func<Type, Type> typeMapper = null)
    {
        var result = new AumlLoadResult { Root = liveRoot };
        var oldRoot = RootElementOf(previous?.Plan);
        if (liveRoot == null || oldRoot == null)
        {
            result.Diagnostics.Add("No live tree to reconcile");
            return result;
        }

        var lowered = Lower(newAumlText, assemblies, result.Diagnostics);
        result.Ast = lowered?.Ast;
        result.Plan = lowered?.Plan;
        var newRoot = RootElementOf(lowered?.Plan);
        if (newRoot == null
            || !string.Equals(oldRoot.Node.TypeReference?.Name, newRoot.Node.TypeReference?.Name, StringComparison.Ordinal))
        {
            return result;
        }

        var executor = new BuildPlanExecutor(lowered.Resolver, lowered.Assemblies, typeMapper, result.Diagnostics);
        try
        {
            new BuildPlanReconciler(executor, lowered.Resolver, result.Diagnostics, liveRoot).Reconcile(liveRoot, oldRoot, newRoot);
            result.Reconciled = true;
        }
        catch (Exception e)
        {
            result.Diagnostics.Add($"Reconcile error: {e.Message}");
        }

        result.SourceMap = executor.SourceMap;
        return result;
    }

    private static ElementStep RootElementOf(BuildPlan plan)
    {
        return plan?.Steps.OfType<ElementStep>().FirstOrDefault();
    }

    private static object CreateRoot(AumlAstObjectNode root, ITypeResolver resolver, Func<Type, Type> typeMapper)
    {
        var type = (resolver.Resolve(root.TypeReference.GetFullTypeName()) as ReflectionResolvedType)?.ClrType
                   ?? throw new TypeLoadException($"Unknown type '{root.TypeReference.GetFullTypeName()}'");
        return Activator.CreateInstance(typeMapper?.Invoke(type) ?? type);
    }

    private static DefaultAumlTransformer Transformer(IEnumerable<Assembly> assemblies) => new()
    {
        LanguageTables = assemblies.Where(a => !a.IsDynamic).SelectMany(LanguageTablesOf).ToList(),
    };

    private static LanguageTableShape[] LanguageTablesOf(Assembly assembly) => TablesByAssembly.GetValue(assembly, static a =>
    {
        if (a != typeof(LocalizedStrings).Assembly && a.GetReferencedAssemblies().All(r => r.Name != typeof(LocalizedStrings).Assembly.GetName().Name))
        {
            return [];
        }

        try
        {
            return a.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(LocalizedStrings)))
                .Select(t => Localize.TableOf(t) is ILanguageTable table
                    ? new LanguageTableShape(t.FullName, table.Keys.ToDictionary(k => k, table.PlaceholdersOf))
                    : null)
                .Where(s => s != null)
                .ToArray();
        }
        catch (ReflectionTypeLoadException)
        {
            return [];
        }
    });
}
