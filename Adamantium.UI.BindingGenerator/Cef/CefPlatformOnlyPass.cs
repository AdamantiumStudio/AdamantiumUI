using QuantumBinding.Generator.AST;
using QuantumBinding.Generator.Processors;
using Delegate = QuantumBinding.Generator.AST.Delegate;

namespace Adamantium.UI.BindingGenerator.Cef;

/// <summary>Leaves a platform's module only what the platform's own headers declare (cef_types_win, cef_types_linux,
/// cef_types_mac, and cef_thread_internal, whose types are the platform's threads): the common API comes from the
/// common module.</summary>
public sealed class CefPlatformOnlyPass : PreGeneratorPass
{
    private readonly IReadOnlySet<string> platformHeaders;

    public CefPlatformOnlyPass(IReadOnlySet<string> platformHeaders)
    {
        this.platformHeaders = platformHeaders;
        Options.VisitEnums = true;
        Options.VisitClasses = true;
        Options.VisitFunctions = true;
        Options.VisitDelegates = true;
    }

    public override bool VisitEnum(Enumeration enumeration) => Keep(enumeration);

    public override bool VisitClass(Class @class) => Keep(@class);

    public override bool VisitFunction(Function function) => Keep(function);

    public override bool VisitDelegate(Delegate @delegate) => Keep(@delegate);

    private bool Keep(Declaration declaration)
    {
        if (!platformHeaders.Contains(declaration.Location?.FileNameWithoutExtension ?? string.Empty))
        {
            declaration.IsIgnored = true;
        }

        return true;
    }
}
