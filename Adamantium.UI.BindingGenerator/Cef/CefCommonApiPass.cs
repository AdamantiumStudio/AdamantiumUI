using QuantumBinding.Generator;
using QuantumBinding.Generator.AST;
using QuantumBinding.Generator.Processors;
using QuantumBinding.Generator.Types;
using Delegate = QuantumBinding.Generator.AST.Delegate;

namespace Adamantium.UI.BindingGenerator.Cef;

/// <summary>Makes the common module the same on every platform. What the platform's headers declare is left to the
/// platform modules, and the common API names it as void*: a pointer to a platform struct (cef_main_args_t,
/// cef_window_info_t, cef_accelerated_paint_info_t) and a handle by value, which is pointer-sized everywhere; a thread
/// id by value as uint, 32 bits everywhere (DWORD on Windows, pid_t elsewhere). Any other platform struct by value is an
/// error. wchar_t is two bytes on Windows and four elsewhere, and CEF's strings are UTF-16: the wide string API is left
/// out, and the one other wchar_t* (cef_version_info_t's libcef_path) is void*. In C wchar_t is a typedef that reaches
/// the pass as an unsigned 16-bit integer, so that field is named rather than found by its type.</summary>
public sealed class CefCommonApiPass : PreGeneratorPass
{
    private const string WideStrings = "_wide";

    private static readonly Dictionary<string, PrimitiveType> PlatformValues = new()
    {
        ["cef_platform_thread_id_t"] = PrimitiveType.UInt32,
    };

    private static readonly HashSet<(string Struct, string Field)> WideCharFields =
    [
        ("_cef_version_info_t", "libcef_path"),
    ];

    private readonly IReadOnlySet<string> platformHeaders;
    private HashSet<string> platformTypes;
    private HashSet<string> platformHandles;

    public CefCommonApiPass(IReadOnlySet<string> platformHeaders)
    {
        this.platformHeaders = platformHeaders;
        Options.VisitEnums = true;
        Options.VisitClasses = true;
        Options.VisitFields = true;
        Options.VisitFunctions = true;
        Options.VisitDelegates = true;
        Options.VisitParameters = true;
    }

    public override void OnInitialize()
    {
        var declarations = AstContext.TranslationUnits.SelectMany(unit => unit.Declarations).Where(InPlatformHeader).ToList();
        platformTypes = declarations.Select(declaration => declaration.Name).ToHashSet();
        platformHandles = declarations.OfType<Class>().Where(@class => @class.ClassType == ClassType.Class)
            .Select(@class => @class.Name).ToHashSet();
    }

    public override bool VisitEnum(Enumeration enumeration) => Drop(enumeration);

    public override bool VisitClass(Class @class) => Drop(@class);

    public override bool VisitField(Field field)
    {
        if (field.Class is { IsIgnored: true })
        {
            return true;
        }

        field.Type = WideCharFields.Contains((field.Class?.Name, field.Name))
            ? VoidPointer(field.Type)
            : Erase(field.Type, false, field.Name);
        return true;
    }

    public override bool VisitFunction(Function function)
    {
        Drop(function);
        if (!function.IsIgnored)
        {
            function.ReturnType = Erase(function.ReturnType, false, function.Name);
        }

        return true;
    }

    public override bool VisitDelegate(Delegate @delegate)
    {
        Drop(@delegate);
        if (!@delegate.IsIgnored)
        {
            @delegate.ReturnType = Erase(@delegate.ReturnType, false, @delegate.Name);
        }

        return true;
    }

    public override bool VisitParameter(Parameter parameter)
    {
        if (parameter.Parent is not Declaration { IsIgnored: true })
        {
            parameter.Type = Erase(parameter.Type, false, parameter.Name);
        }

        return true;
    }

    private bool InPlatformHeader(Declaration declaration)
    {
        return platformHeaders.Contains(declaration.Location?.FileNameWithoutExtension ?? string.Empty);
    }

    private bool Drop(Declaration declaration)
    {
        if (InPlatformHeader(declaration) || declaration.Name.Contains(WideStrings, StringComparison.Ordinal))
        {
            declaration.IsIgnored = true;
        }

        return true;
    }

    private BindingType Erase(BindingType type, bool behindPointer, string owner)
    {
        switch (type)
        {
            case CustomType custom when PlatformValues.TryGetValue(custom.Name, out var primitive):
                return new BuiltinType(primitive);
            case CustomType custom when platformHandles.Contains(custom.Name):
                return VoidPointer(type);
            case CustomType custom when platformTypes.Contains(custom.Name):
                if (!behindPointer)
                {
                    throw new InvalidOperationException(
                        $"{owner} takes the platform struct {custom.Name} by value, which the common API cannot.");
                }

                return new BuiltinType(PrimitiveType.Void);
            case PointerType pointer:
                var pointee = Erase(pointer.Pointee, true, owner);
                if (!ReferenceEquals(pointee, pointer.Pointee))
                {
                    pointer.Pointee = pointee;
                    pointer.Declaration = null;
                }

                return pointer;
            case ArrayType array:
                array.ElementType = Erase(array.ElementType, false, owner);
                return array;
            case DelegateType functionPointer:
                foreach (var parameter in functionPointer.Parameters)
                {
                    parameter.Type = Erase(parameter.Type, false, owner);
                }

                return functionPointer;
            default:
                return type;
        }
    }

    private static PointerType VoidPointer(BindingType replaced)
    {
        var pointer = new PointerType { Pointee = new BuiltinType(PrimitiveType.Void) };
        pointer.Qualifiers.IsConst = replaced.Qualifiers.IsConst;
        return pointer;
    }
}
