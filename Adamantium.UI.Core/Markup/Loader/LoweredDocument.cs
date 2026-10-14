using System.Reflection;
using Adamantium.UI.Markup;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.Building;
using Adamantium.UI.Markup.CodeGeneration;

namespace Adamantium.UI.Core.Markup;

internal sealed class LoweredDocument(AumlAstObjectNode ast, EntityType entityType, BuildPlan plan, ITypeResolver resolver,
    List<Assembly> assemblies)
{
    public AumlAstObjectNode Ast { get; } = ast;

    public EntityType EntityType { get; } = entityType;

    public BuildPlan Plan { get; } = plan;

    public ITypeResolver Resolver { get; } = resolver;

    public List<Assembly> Assemblies { get; } = assemblies;
}
