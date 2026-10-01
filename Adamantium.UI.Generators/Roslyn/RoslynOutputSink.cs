using Adamantium.UI.Markup.CodeGeneration;
using Microsoft.CodeAnalysis;

namespace Adamantium.UI.Generators.Roslyn;

public class RoslynOutputSink(SourceProductionContext context) : ICodeOutputSink
{
    private readonly SourceProductionContext _context = context;

    public void Emit(string hintName, string code)
    {
        _context.AddSource($"{hintName}.g.cs", code);
    }
}