using System.Globalization;
using System.Reflection;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.Building;
using Adamantium.UI.Markup.CodeGeneration;
using Adamantium.UI.Markup.CodeGeneration.Reflection;

namespace Adamantium.UI.Core.Markup;

internal sealed class BuildPlanExecutor
{
    private const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.Static;

    private static readonly MethodInfo TypedFactory =
        typeof(BuildPlanExecutor).GetMethod(nameof(Typed), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly Dictionary<string, Type> Keywords = new(StringComparer.Ordinal)
    {
        ["object"] = typeof(object),
        ["string"] = typeof(string),
        ["bool"] = typeof(bool),
        ["double"] = typeof(double),
        ["float"] = typeof(float),
        ["int"] = typeof(int),
        ["long"] = typeof(long),
    };

    private readonly ITypeResolver _resolver;
    private readonly List<Assembly> _assemblies;
    private readonly Func<Type, Type> _typeMapper;
    private readonly List<string> _diagnostics;
    private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _fields = new(StringComparer.Ordinal);
    private object _root;
    private int _functionDepth;

    public BuildPlanExecutor(ITypeResolver resolver, List<Assembly> assemblies, Func<Type, Type> typeMapper, List<string> diagnostics)
    {
        _resolver = resolver;
        _assemblies = assemblies;
        _typeMapper = typeMapper;
        _diagnostics = diagnostics;
    }

    public Dictionary<object, AumlSourceSpan> SourceMap { get; } = new(ReferenceEqualityComparer.Instance);

    public object Run(BuildPlan plan, object root)
    {
        _root = root;
        return RunBody(plan, new BuildFrame(null)) ?? _root;
    }

    public BuildFrame RunPart(BuildScope part, object root, string variable, object value)
    {
        _root = root;
        var frame = new BuildFrame(null);
        frame.Set(variable, value);
        Hoist(part, frame);
        Run(part, frame);
        return frame;
    }

    public void Unsubscribe(SubscribeStep subscribe, object root, string variable, object value)
    {
        _root = root;
        var frame = new BuildFrame(null);
        frame.Set(variable, value);
        Guarded($"Event '{subscribe.EventName}'", () =>
        {
            var (target, eventInfo, handler) = Handler(subscribe, frame);
            eventInfo.RemoveEventHandler(target, handler);
        });
    }

    private object RunBody(BuildScope body, BuildFrame frame)
    {
        Hoist(body, frame);
        RunSteps(body, frame);
        return frame.ReturnValue;
    }

    private static void Hoist(BuildScope scope, BuildFrame frame)
    {
        foreach (var step in scope.Steps)
        {
            if (step is FunctionStep function)
            {
                frame.Set(function.Name, new BuildFunction(function, frame));
            }
            else if (step is BuildScope nested)
            {
                Hoist(nested, frame);
            }
        }
    }

    private void RunSteps(BuildScope scope, BuildFrame frame)
    {
        foreach (var step in scope.Steps)
        {
            if (frame.HasReturned)
            {
                return;
            }

            Run(step, frame);
        }
    }

    private void Run(BuildStep step, BuildFrame frame)
    {
        switch (step)
        {
            case BlankLineStep:
            case FunctionStep:
                break;
            case ElementStep element:
                Guarded($"Element '{element.Node.TypeReference?.Name}' (line {element.Node.Line})", () => RunSteps(element, frame));
                if (_functionDepth == 0 && TryGetVariable(element.Variable, frame, out var built) && built != null
                    && !SourceMap.ContainsKey(built))
                {
                    SourceMap[built] = new AumlSourceSpan(element.Node.Line, element.Node.Position);
                }

                break;
            case PropertyStep property:
                Guarded($"Property '{(property.Node.Property as AumlAstPropertyReference)?.Name}' (line {property.Node.Line})",
                    () => RunSteps(property, frame));
                break;
            case ChildStep child:
                Guarded($"Element '{child.Node.TypeReference?.Name}' (line {child.Node.Line})", () => RunSteps(child, frame));
                break;
            case BuildScope scope:
                RunSteps(scope, frame);
                break;
            case DeclareStep declare:
                Declare(declare, frame);
                break;
            case AssignStep assign:
                Assign(assign, frame);
                break;
            case IndexAssignStep index:
                AssignIndex(index, frame);
                break;
            case InvokeStep invoke:
                Call(invoke.Call, frame);
                break;
            case SubscribeStep subscribe:
                Subscribe(subscribe, frame);
                break;
            case ReturnStep returned:
                frame.Return(Evaluate(returned.Value, null, frame));
                break;
            default:
                throw new NotSupportedException($"A build step the loader does not know: {step.GetType().Name}");
        }
    }

    private void Guarded(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            var message = $"{what}: {InnermostMessage(e)}";
            _diagnostics.Add(message);
            if (_functionDepth > 0)
            {
                Serilog.Log.Logger.Warning("Markup: a template or a deferred element did not build whole: {Message}", message);
            }
        }
    }

    private void Declare(DeclareStep declare, BuildFrame frame)
    {
        var value = Evaluate(declare.Value, null, frame);
        _root ??= value;
        if (!declare.IsField)
        {
            frame.Set(declare.Variable, value);
            return;
        }

        _fields[declare.Variable] = value;
        var field = _root?.GetType().GetField(declare.Variable, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null && (value == null || field.FieldType.IsInstanceOfType(value)))
        {
            field.SetValue(_root, value);
        }
    }

    private void Assign(AssignStep assign, BuildFrame frame)
    {
        if (assign.Target is TypeValue staticOwner)
        {
            var ownerType = ResolveType(staticOwner);
            var staticProperty = FindProperty(ownerType, assign.Member, StaticMembers);
            if (staticProperty != null)
            {
                staticProperty.SetValue(null, Evaluate(assign.Value, staticProperty.PropertyType, frame));
                return;
            }

            var staticField = ownerType.GetField(assign.Member, StaticMembers)
                              ?? throw new MissingMemberException(ownerType.Name, assign.Member);
            staticField.SetValue(null, Evaluate(assign.Value, staticField.FieldType, frame));
            return;
        }

        var target = assign.Target == null ? _root : Evaluate(assign.Target, null, frame);
        if (target == null)
        {
            throw new NullReferenceException($"No object to set '{assign.Member}' on");
        }

        var property = FindProperty(target.GetType(), assign.Member, InstanceMembers);
        if (property != null)
        {
            property.SetValue(target, Evaluate(assign.Value, property.PropertyType, frame));
            return;
        }

        var field = target.GetType().GetField(assign.Member, InstanceMembers)
                    ?? throw new MissingMemberException(target.GetType().Name, assign.Member);
        field.SetValue(target, Evaluate(assign.Value, field.FieldType, frame));
    }

    private void AssignIndex(IndexAssignStep index, BuildFrame frame)
    {
        var target = Evaluate(index.Target, null, frame);
        var indexer = target.GetType().GetProperties(InstanceMembers).FirstOrDefault(p => p.GetIndexParameters().Length == 1)
                      ?? throw new MissingMemberException(target.GetType().Name, "this[]");
        var key = Evaluate(index.Key, indexer.GetIndexParameters()[0].ParameterType, frame);
        indexer.SetValue(target, Evaluate(index.Value, indexer.PropertyType, frame), [key]);
    }

    private void Subscribe(SubscribeStep subscribe, BuildFrame frame)
    {
        var (target, eventInfo, handler) = Handler(subscribe, frame);
        eventInfo.AddEventHandler(target, handler);
    }

    private (object Target, EventInfo Event, Delegate Handler) Handler(SubscribeStep subscribe, BuildFrame frame)
    {
        var target = subscribe.Target == null ? _root : Evaluate(subscribe.Target, null, frame);
        var eventInfo = target.GetType().GetEvent(subscribe.EventName, InstanceMembers)
                        ?? throw new MissingMemberException(target.GetType().Name, subscribe.EventName);
        var method = _root.GetType().GetMethod(subscribe.Handler, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     ?? throw new MissingMethodException(_root.GetType().Name, subscribe.Handler);
        return (target, eventInfo, Delegate.CreateDelegate(eventInfo.EventHandlerType, _root, method));
    }

    private object Evaluate(BuildValue value, Type expected, BuildFrame frame)
    {
        var result = value switch
        {
            VariableValue variable => TryGetVariable(variable.Name, frame, out var found)
                ? found
                : throw new InvalidOperationException($"'{variable.Name}' is not defined"),
            MemberValue member => ReadMember(member, frame),
            LiteralValue literal => ConvertLiteral(literal.Text, expected),
            StringValue text => text.Text,
            NullValue => null,
            EnumValue enumValue => CombineEnum(enumValue),
            NewValue created => Create(created, frame),
            CallValue call => Call(call, frame),
            CastValue cast => Evaluate(cast.Inner, ResolveType(cast.Type), frame),
            TypeOfValue typeOf => ResolveType(typeOf.Type),
            NameOfValue nameOf => nameOf.Member.Member,
            _ => throw new NotSupportedException($"A build value the loader does not know: {value.GetType().Name}")
        };

        return expected == null ? result : Coerce(result, expected);
    }

    private bool TryGetVariable(string name, BuildFrame frame, out object value)
    {
        if (name == VariableValue.This.Name)
        {
            value = _root;
            return true;
        }

        return frame.TryGet(name, out value) || _fields.TryGetValue(name, out value);
    }

    private object ReadMember(MemberValue member, BuildFrame frame)
    {
        if (member.Owner is TypeValue staticOwner)
        {
            var ownerType = ResolveType(staticOwner);
            var property = FindProperty(ownerType, member.Member, StaticMembers);
            if (property != null)
            {
                return property.GetValue(null);
            }

            var field = ownerType.GetField(member.Member, StaticMembers) ?? throw new MissingMemberException(ownerType.Name, member.Member);
            return field.GetValue(null);
        }

        var owner = member.Owner == null ? _root : Evaluate(member.Owner, null, frame);
        var ownerView = member.Owner is CastValue cast ? ResolveType(cast.Type) : owner.GetType();
        var instanceProperty = FindProperty(ownerView, member.Member, InstanceMembers);
        if (instanceProperty != null)
        {
            return instanceProperty.GetValue(owner);
        }

        var instanceField = ownerView.GetField(member.Member, InstanceMembers)
                            ?? throw new MissingMemberException(ownerView.Name, member.Member);
        return instanceField.GetValue(owner);
    }

    private object CombineEnum(EnumValue enumValue)
    {
        var type = ResolveType(enumValue.Type);
        long combined = 0;
        foreach (var member in enumValue.Members)
        {
            combined |= Convert.ToInt64(Enum.Parse(type, member), CultureInfo.InvariantCulture);
        }

        return Enum.ToObject(type, combined);
    }

    private object Create(NewValue created, BuildFrame frame)
    {
        var type = ResolveType(created.Type);
        type = _typeMapper?.Invoke(type) ?? type;
        var arguments = Prepare(created.Arguments, frame);
        var constructor = Best(type.GetConstructors(), arguments);
        if (constructor.Method == null)
        {
            throw new MissingMethodException($"{type.Name} has no constructor taking ({Describe(arguments)})");
        }

        return ((ConstructorInfo)constructor.Method).Invoke(constructor.Arguments);
    }

    private object Call(CallValue call, BuildFrame frame)
    {
        object target;
        Type view;
        BindingFlags flags;
        if (call.Target is TypeValue staticOwner)
        {
            target = null;
            view = ResolveType(staticOwner);
            flags = StaticMembers;
        }
        else
        {
            target = call.Target == null ? _root : Evaluate(call.Target, null, frame);
            if (target == null)
            {
                throw new NullReferenceException($"No object to call '{call.Method}' on");
            }

            view = call.Target is CastValue cast ? ResolveType(cast.Type) : target.GetType();
            flags = InstanceMembers;
        }

        var typeArguments = call.TypeArguments.Select(ResolveType).ToArray();
        var methods = MethodsOf(view, flags)
            .Where(m => m.Name == call.Method && m.GetGenericArguments().Length == typeArguments.Length)
            .Select(m => typeArguments.Length > 0 ? m.MakeGenericMethod(typeArguments) : m)
            .ToArray();
        var arguments = Prepare(call.Arguments, frame);
        var method = Best(methods, arguments);
        if (method.Method == null)
        {
            throw new MissingMethodException($"{view.Name} has no method {call.Method}({Describe(arguments)})");
        }

        return method.Method.Invoke(target, method.Arguments);
    }

    private static IEnumerable<MethodInfo> MethodsOf(Type type, BindingFlags flags)
    {
        var methods = type.GetMethods(flags).AsEnumerable();
        if (type.IsInterface)
        {
            methods = methods.Concat(type.GetInterfaces().SelectMany(i => i.GetMethods(flags)));
        }

        return methods;
    }

    private List<PreparedArgument> Prepare(IReadOnlyList<BuildValue> arguments, BuildFrame frame)
    {
        return arguments
            .Select(argument => argument is LiteralValue literal
                ? new PreparedArgument(literal.Text, null, true)
                : new PreparedArgument(null, Evaluate(argument, null, frame), false))
            .ToList();
    }

    private (MethodBase Method, object[] Arguments) Best(IEnumerable<MethodBase> candidates, List<PreparedArgument> arguments)
    {
        MethodBase best = null;
        object[] bestArguments = null;
        var bestScore = -1;
        foreach (var candidate in candidates)
        {
            if (TryBind(candidate.GetParameters(), arguments, out var bound, out var score) && score > bestScore)
            {
                best = candidate;
                bestArguments = bound;
                bestScore = score;
            }
        }

        return (best, bestArguments);
    }

    private bool TryBind(ParameterInfo[] parameters, List<PreparedArgument> arguments, out object[] bound, out int score)
    {
        bound = null;
        score = 0;
        if (arguments.Count > parameters.Length || parameters.Skip(arguments.Count).Any(p => !p.IsOptional))
        {
            return false;
        }

        var values = new object[parameters.Length];
        for (var i = arguments.Count; i < parameters.Length; i++)
        {
            values[i] = parameters[i].DefaultValue;
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            var parameterType = parameters[i].ParameterType;
            var argument = arguments[i];
            if (argument.IsLiteral)
            {
                if (!TryConvertLiteral(argument.Literal, parameterType, out values[i]))
                {
                    return false;
                }

                score += 1;
                continue;
            }

            var value = argument.Value;
            if (value == null)
            {
                if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                {
                    return false;
                }

                score += 1;
                continue;
            }

            if (value.GetType() == parameterType)
            {
                values[i] = value;
                score += 3;
            }
            else if (parameterType.IsInstanceOfType(value))
            {
                values[i] = value;
                score += 2;
            }
            else if (value is BuildFunction function && typeof(Delegate).IsAssignableFrom(parameterType))
            {
                values[i] = MakeDelegate(function, parameterType);
                score += 2;
            }
            else
            {
                return false;
            }
        }

        bound = values;
        return true;
    }

    private object Coerce(object value, Type expected)
    {
        if (value == null || expected.IsInstanceOfType(value))
        {
            return value;
        }

        if (value is BuildFunction function && typeof(Delegate).IsAssignableFrom(expected))
        {
            return MakeDelegate(function, expected);
        }

        var target = Nullable.GetUnderlyingType(expected) ?? expected;
        if (value is IConvertible && (target.IsPrimitive || target == typeof(decimal)))
        {
            return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }

        throw new InvalidCastException($"{value.GetType().Name} cannot be used as {expected.Name}");
    }

    private Delegate MakeDelegate(BuildFunction function, Type delegateType)
    {
        var returnType = delegateType.GetMethod("Invoke").ReturnType;
        var factory = (Delegate)TypedFactory.MakeGenericMethod(returnType)
            .Invoke(null, [(Func<object>)(() => CallFunction(function))]);
        return factory.GetType() == delegateType
            ? factory
            : Delegate.CreateDelegate(delegateType, factory, factory.GetType().GetMethod("Invoke"));
    }

    private object CallFunction(BuildFunction function)
    {
        _functionDepth++;
        try
        {
            return RunBody(function.Step, new BuildFrame(function.Frame));
        }
        finally
        {
            _functionDepth--;
        }
    }

    private static Func<T> Typed<T>(Func<object> call)
    {
        return () => (T)call();
    }

    private static object ConvertLiteral(string text, Type expected)
    {
        if (expected != null && expected != typeof(object))
        {
            return TryConvertLiteral(text, expected, out var converted)
                ? converted
                : throw new FormatException($"'{text}' is not a {expected.Name}");
        }

        if (bool.TryParse(text, out var flag))
        {
            return flag;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        return double.Parse(text, CultureInfo.InvariantCulture);
    }

    private static bool TryConvertLiteral(string text, Type expected, out object value)
    {
        value = null;
        var target = Nullable.GetUnderlyingType(expected) ?? expected;
        if (target == typeof(object))
        {
            value = ConvertLiteral(text, null);
            return true;
        }

        if (target == typeof(bool))
        {
            if (!bool.TryParse(text, out var flag))
            {
                return false;
            }

            value = flag;
            return true;
        }

        if (!target.IsPrimitive && target != typeof(decimal))
        {
            return false;
        }

        try
        {
            value = Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            return false;
        }
    }

    private static PropertyInfo FindProperty(Type type, string name, BindingFlags flags)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var property = current.GetProperty(name, flags | BindingFlags.DeclaredOnly);
            if (property != null)
            {
                return property;
            }
        }

        return type.IsInterface
            ? type.GetInterfaces().Select(i => i.GetProperty(name, flags)).FirstOrDefault(p => p != null)
            : null;
    }

    private Type ResolveType(TypeValue type)
    {
        if (type.Resolved is ReflectionResolvedType reflected)
        {
            return reflected.ClrType;
        }

        if (_types.TryGetValue(type.QualifiedName, out var known))
        {
            return known;
        }

        var resolved = ResolveTypeByName(type.QualifiedName)
                       ?? throw new TypeLoadException($"Type '{type.QualifiedName}' is not found");
        _types[type.QualifiedName] = resolved;
        return resolved;
    }

    private Type ResolveTypeByName(string qualifiedName)
    {
        if (Keywords.TryGetValue(qualifiedName, out var keyword))
        {
            return keyword;
        }

        const string globalPrefix = "global::";
        var fullName = qualifiedName.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? qualifiedName.Substring(globalPrefix.Length)
            : qualifiedName;
        if (_resolver.Resolve(fullName) is ReflectionResolvedType reflected)
        {
            return reflected.ClrType;
        }

        return _assemblies
            .SelectMany(ReflectionResolvedAssembly.SafeGetTypes)
            .FirstOrDefault(t => t.FullName?.Replace('+', '.') == fullName);
    }

    private static string Describe(List<PreparedArgument> arguments)
    {
        return string.Join(", ", arguments.Select(a => a.IsLiteral ? a.Literal : a.Value?.GetType().Name ?? "null"));
    }

    private static string InnermostMessage(Exception e)
    {
        while (e is TargetInvocationException { InnerException: not null } wrapped)
        {
            e = wrapped.InnerException;
        }

        return e.Message;
    }
}
