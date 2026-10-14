using Adamantium.Core;

namespace Adamantium.UI.Markup.Building;

/// <summary>Writes a build plan as the C# statements of a generated class.</summary>
public static class BuildPlanEmitter
{
    /// <summary>Writes every step of <paramref name="scope"/> to <paramref name="text"/> at its current indentation.</summary>
    public static void Emit(BuildScope scope, TextGenerator text)
    {
        foreach (var step in scope.Steps)
        {
            Emit(step, text);
        }
    }

    /// <summary>The C# expression for <paramref name="value"/>.</summary>
    public static string Print(BuildValue value)
    {
        return value switch
        {
            VariableValue variable => variable.Name,
            TypeValue type => type.QualifiedName,
            MemberValue { Owner: null } member => member.Member,
            MemberValue member => $"{PrintOwner(member.Owner)}.{member.Member}",
            LiteralValue literal => literal.Text,
            StringValue { Escaped: true } text => Quote(text.Text),
            StringValue text => $"\"{text.Text}\"",
            NullValue => "null",
            EnumValue enumValue => string.Join(" | ", enumValue.Members.Select(member => $"{enumValue.Type.QualifiedName}.{member}")),
            NewValue created => $"new {created.Type.QualifiedName}({PrintArguments(created.Arguments)})",
            CallValue call => PrintCall(call),
            CastValue { InnerParenthesized: true } cast => $"({cast.Type.QualifiedName})({Print(cast.Inner)})",
            CastValue cast => $"({cast.Type.QualifiedName}){Print(cast.Inner)}",
            TypeOfValue typeOf => $"typeof({typeOf.Type.QualifiedName})",
            NameOfValue nameOf => $"nameof({Print(nameOf.Member)})",
            _ => throw new NotSupportedException($"A build value the emitter does not know: {value.GetType().Name}")
        };
    }

    private static void Emit(BuildStep step, TextGenerator text)
    {
        switch (step)
        {
            case BlankLineStep:
                text.NewLine();
                break;
            case DeclareStep { IsField: true } declare:
                text.WriteLine($"{declare.Variable} = {Print(declare.Value)};");
                break;
            case DeclareStep declare:
                text.WriteLine($"var {declare.Variable} = {Print(declare.Value)};");
                break;
            case AssignStep assign:
                text.WriteLine($"{PrintTarget(assign.Target)}{assign.Member} = {Print(assign.Value)};");
                break;
            case IndexAssignStep index:
                text.WriteLine($"{Print(index.Target)}[{Print(index.Key)}] = {Print(index.Value)};");
                break;
            case InvokeStep invoke:
                text.WriteLine($"{PrintCall(invoke.Call)};");
                break;
            case SubscribeStep subscribe:
                text.WriteLine($"{PrintTarget(subscribe.Target)}{subscribe.EventName} += {subscribe.Handler};");
                break;
            case ReturnStep returned:
                text.WriteLine($"return {Print(returned.Value)};");
                break;
            case ConditionalStep conditional:
                text.WriteLine($"#if {conditional.Symbol}");
                Emit(conditional, text);
                text.WriteLine("#endif");
                break;
            case FunctionStep function:
                text.WriteLine($"{function.ReturnType.QualifiedName} {function.Name}()");
                text.WriteOpenBraceAndIndent();
                Emit(function, text);
                text.UnindentAndWriteCloseBrace();
                break;
            case BuildScope scope:
                Emit(scope, text);
                break;
            default:
                throw new NotSupportedException($"A build step the emitter does not know: {step.GetType().Name}");
        }
    }

    private static string PrintCall(CallValue call)
    {
        var typeArguments = call.TypeArguments.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", call.TypeArguments.Select(type => type.QualifiedName))}>";
        return $"{PrintTarget(call.Target)}{call.Method}{typeArguments}({PrintArguments(call.Arguments)})";
    }

    private static string PrintTarget(BuildValue target)
    {
        return target == null ? string.Empty : $"{PrintOwner(target)}.";
    }

    private static string PrintOwner(BuildValue owner)
    {
        return owner is CastValue ? $"({Print(owner)})" : Print(owner);
    }

    private static string PrintArguments(IReadOnlyList<BuildValue> arguments)
    {
        return string.Join(", ", arguments.Select(Print));
    }

    private static string Quote(string text)
    {
        if (text == null)
        {
            return "null";
        }

        var escaped = text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

        return $"\"{escaped}\"";
    }
}
