using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Adamantium.UI.Automation;
using Adamantium.UI.Markup.Localization;
using Microsoft.CodeAnalysis.CSharp;

namespace Adamantium.UI.Generators.Scenarios;

/// <summary>Reads a scenario file - checked against its schema, every selector path read - into the C# of its steps,
/// each a call of the automation library's typed API.</summary>
public static class ScenarioTranslator
{
    public const string Extension = ".ascenario";

    private const string Automation = "global::Adamantium.UI.Automation.";

    private static readonly string[] ElementKeys =
    [
        "Name", "Id", "Type", "Class", "Value", "Toggle", "Selected", "Expanded", "Min", "Max", "HScroll", "VScroll", "Window",
        "Zoom", "Dock", "Key", "Left", "Top", "Width", "Height", "Enabled", "Offscreen", "Focus"
    ];

    private static readonly string[] ReservedNames =
        ["Scenarios", "Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone", "Finalize", "ReferenceEquals"];

    public static ScenarioFile Read(string path, string text)
    {
        var problems = new List<ScenarioProblem>();
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        XDocument document;
        try
        {
            document = XDocument.Load(new StringReader(text), LoadOptions.SetLineInfo);
        }
        catch (XmlException e)
        {
            problems.Add(new ScenarioProblem("AUS001", MarkupMessages.ScenarioNotXml(e.Message), e.LineNumber, e.LinePosition));
            return new ScenarioFile(path, name, 1, null, null, [], problems);
        }

        var root = document.Root;
        if (root.Name != ScenarioSchema.Namespace + "Scenario")
        {
            var (line, column) = Where(root);
            problems.Add(new ScenarioProblem("AUS002", MarkupMessages.ScenarioRootNotScenario(ScenarioSchema.Namespace.NamespaceName), line, column));
            return new ScenarioFile(path, name, line, null, null, [], problems);
        }

        document.Validate(ScenarioSchema.Set, (sender, e) =>
        {
            var (line, column) = Where(sender as XObject);
            problems.Add(new ScenarioProblem("AUS002", e.Message, line, column));
        });

        var nameLine = Where(root).Line;
        if (root.Attribute("Name") is { } named)
        {
            name = named.Value;
            nameLine = Where(named).Line;
        }

        if (!SyntaxFacts.IsValidIdentifier(name) || SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || ReservedNames.Contains(name))
        {
            problems.Add(new ScenarioProblem("AUS003", MarkupMessages.ScenarioNameNotIdentifier(name), nameLine, 1));
        }

        var steps = new List<string>();
        if (problems.Count == 0)
        {
            foreach (var step in root.Elements())
            {
                try
                {
                    steps.Add(Step(step, path));
                }
                catch (ScenarioStepException e)
                {
                    var (line, column) = Where(e.Node ?? step);
                    problems.Add(new ScenarioProblem(e.Id, e.Message, line, column));
                }
            }
        }

        var platforms = root.Attribute("Platforms")?.Value.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return new ScenarioFile(path, name, nameLine, root.Attribute("Description")?.Value, root.Attribute("Explicit")?.Value, steps,
            problems, platforms);
    }

    private static string Step(XElement step, string path)
    {
        var call = step.Name.LocalName switch
        {
            "Invoke" => $"{Target(step)}.InvokeAsync()",
            "Toggle" => $"{Target(step)}.ToggleAsync()",
            "Select" => $"{Target(step)}.{(Flag(step, "Add") ? "AddToSelectionAsync" : "SelectAsync")}()",
            "Unselect" => $"{Target(step)}.RemoveFromSelectionAsync()",
            "Click" => $"{Target(step)}.ClickAsync()",
            "RightClick" => $"{Target(step)}.RightClickAsync()",
            "Hover" => $"{Target(step)}.HoverAsync()",
            "Expand" => $"{Target(step)}.ExpandAsync()",
            "Collapse" => $"{Target(step)}.CollapseAsync()",
            "ContextMenu" => $"{Target(step)}.ShowContextMenuAsync()",
            "Set" => $"{Target(step)}.SetValueAsync({Literal(step.Attribute("Value").Value)})",
            "Caret" => Caret(step),
            "Type" => $"{Focused(step)}.TypeAsync({Literal(step.Attribute("Text").Value)})",
            "Key" => $"{Focused(step)}.PressKeysAsync({Literal(step.Attribute("Keys").Value)})",
            "Scroll" => Scroll(step),
            "Move" => $"{Target(step)}.MoveByAsync({Pair(step, "By")})",
            "Resize" => $"{Target(step)}.ResizeAsync({Pair(step, "Size")})",
            "Pan" => $"{Target(step)}.PanAsync({Pair(step, "By")})",
            "Zoom" => $"{Target(step)}.ZoomAsync({Number(step, "Percent")})",
            "Wheel" => $"{Target(step)}.WheelAsync({Number(step, "Notches")})",
            "Drag" => $"{Target(step)}.DragAsync({Pair(step, "From")}, {Pair(step, "To")})",
            "Drop" => $"{Target(step)}.DropOntoAsync({Find(step.Attribute("Onto"))}, {Automation}DropSide.{step.Attribute("Side")?.Value ?? "Onto"})",
            "Connect" => $"{Target(step)}.ConnectAsync({Find(step.Attribute("To"))})",
            "Disconnect" => $"{Target(step)}.DisconnectAsync({(step.Attribute("From") is { } from ? Find(from) : "null")})",
            "Dock" => $"{Target(step)}.DockAsync(global::Adamantium.UI.Core.Automation.DockPosition.{step.Attribute("Position").Value}, " +
                      $"{(step.Attribute("Beside") is { } beside ? Find(beside) : "null")})",
            "Window" => Window(step),
            "Touch" => Touch(step),
            "Pen" => Pen(step),
            "Touches" => Touches(step),
            "Pinch" => $"{Target(step)}.PinchAsync({Number(step, "Scale")}, {Number(step, "Rotate", "0")}, " +
                       $"{(step.Attribute("Pan") != null ? Pair(step, "Pan") : "0, 0")}, {Flag(step, "ThroughSystem").ToString().ToLowerInvariant()})",
            "Wait" => Wait(step),
            "WaitIdle" => "session.WaitForIdleAsync()",
            "Expect" => $"{Target(step)}.ExpectAsync({Expectations(step, true)})",
            "Absent" => $"{Target(step)}.ExpectAbsentAsync()",
            _ => throw new InvalidOperationException(step.Name.LocalName)
        };

        var line = $"#line {Where(step).Line} \"{path}\"\n";
        return Flag(step, "AllowErrors")
            ? $"{line}session.AllowErrors = true;\n{line}await {call};\n{line}session.AllowErrors = false;"
            : $"{line}await {call};";
    }

    private static string Caret(XElement step)
    {
        var (line, column) = TextPosition(step.Attribute("At").Value);
        if (step.Attribute("To") is not { } to)
        {
            return $"{Target(step)}.PlaceCaretAsync({line}, {column})";
        }

        var (toLine, toColumn) = TextPosition(to.Value);
        return $"{Target(step)}.SelectTextAsync({line}, {column}, {toLine}, {toColumn})";
    }

    private static string Scroll(XElement step)
    {
        var horizontal = step.Attribute("Horizontal");
        var vertical = step.Attribute("Vertical");
        return horizontal == null && vertical == null
            ? $"{Target(step)}.ScrollIntoViewAsync()"
            : $"{Target(step)}.ScrollToAsync({Number(step, "Horizontal", "null")}, {Number(step, "Vertical", "null")})";
    }

    private static string Window(XElement step) => step.Attribute("Action").Value switch
    {
        "Minimize" => $"{Target(step)}.SetWindowStateAsync(global::Adamantium.UI.Core.WindowState.Minimized)",
        "Maximize" => $"{Target(step)}.SetWindowStateAsync(global::Adamantium.UI.Core.WindowState.Maximized)",
        "Restore" => $"{Target(step)}.SetWindowStateAsync(global::Adamantium.UI.Core.WindowState.Normal)",
        _ => $"{Target(step)}.CloseAsync()"
    };

    private static string Touch(XElement step)
    {
        var system = $"throughSystem: {Flag(step, "ThroughSystem").ToString().ToLowerInvariant()}";
        return Gesture(step) switch
        {
            "Tap" => $"{Target(step)}.TapAsync({system})",
            "Hold" => $"{Target(step)}.HoldAsync({system})",
            _ => $"{Target(step)}.TouchDragAsync({Pair(step, "From")}, {Pair(step, "To")}, {system})"
        };
    }

    private static string Pen(XElement step)
    {
        var gesture = Gesture(step);
        var system = $"throughSystem: {Flag(step, "ThroughSystem").ToString().ToLowerInvariant()}";
        var buttons = $"barrel: {Flag(step, "Barrel").ToString().ToLowerInvariant()}, eraser: {Flag(step, "Eraser").ToString().ToLowerInvariant()}";
        var pressure = step.Attribute("Pressure");
        var range = pressure?.Value.Split([".."], StringSplitOptions.None);
        if (range is { Length: 2 } && gesture != "Drag")
        {
            throw new ScenarioStepException("AUS005", MarkupMessages.ScenarioOnlyForDrag(step.Name.LocalName, "Pressure=\"a..b\""), pressure);
        }

        var single = pressure == null ? "0.5f" : $"{Float(range[0])}";
        switch (gesture)
        {
            case "Tap":
                return $"{Target(step)}.PenTapAsync({single}, {buttons}, {system})";
            case "Hold":
                if (Flag(step, "Barrel") || Flag(step, "Eraser"))
                {
                    throw new ScenarioStepException("AUS006", MarkupMessages.ScenarioHeldPenButtons(), step);
                }

                return $"{Target(step)}.PenHoldAsync({single}, {system})";
            default:
                return range is { Length: 2 }
                    ? $"{Target(step)}.PenDragAsync({Pair(step, "From")}, {Pair(step, "To")}, {Float(range[0])}, {Float(range[1])}, {buttons}, {system})"
                    : $"{Target(step)}.PenDragAsync({Pair(step, "From")}, {Pair(step, "To")}, {single}, {buttons}, {system})";
        }
    }

    private static string Touches(XElement step)
    {
        var fingers = $"new {Automation}AutomationElement[] {{ {string.Join(", ", step.Elements().Select(finger => Find(finger.Attribute("Target"))))} }}";
        var system = $"throughSystem: {Flag(step, "ThroughSystem").ToString().ToLowerInvariant()}";
        if (step.Attribute("Gesture").Value != "Drag" && step.Attribute("By") is { } by)
        {
            throw new ScenarioStepException("AUS005", MarkupMessages.ScenarioOnlyForDrag(step.Name.LocalName, "By"), by);
        }

        switch (step.Attribute("Gesture").Value)
        {
            case "Tap":
                return $"session.TapTogetherAsync({fingers}, {system})";
            case "Hold":
                return $"session.HoldTogetherAsync({fingers}, {system})";
            default:
                if (step.Attribute("By") == null)
                {
                    throw new ScenarioStepException("AUS005", MarkupMessages.ScenarioDragNeeds(step.Name.LocalName, "By"), step);
                }

                return $"session.TouchDragTogetherAsync({fingers}, {Pair(step, "By")}, {system})";
        }
    }

    private static string Wait(XElement step)
    {
        var timeout = step.Attribute("Timeout") is { } limit ? $", {Duration(limit.Value)}" : string.Empty;
        return Keys(step).Any()
            ? $"{Target(step)}.WaitUntilAsync(new[] {{ {Expectations(step, false)} }}{timeout})"
            : $"{Target(step)}.WaitForAsync({timeout.TrimStart(',', ' ')})";
    }

    private static string Expectations(XElement step, bool required)
    {
        var keys = Keys(step).ToList();
        if (keys.Count == 0 && required)
        {
            throw new ScenarioStepException("AUS007", MarkupMessages.ScenarioChecksNothing(step.Name.LocalName), step);
        }

        return string.Join(", ", keys.Select(key =>
            $"new {Automation}Expectation({Literal(ElementKeys.Contains(key.Name.LocalName) ? key.Name.LocalName.ToLowerInvariant() : key.Name.LocalName)}, " +
            $"{Literal(key.Value)})"));
    }

    private static IEnumerable<XAttribute> Keys(XElement step)
    {
        var keys = step.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.LocalName != "Target" &&
                                !(step.Name.LocalName == "Wait" && attribute.Name.LocalName == "Timeout"))
            .ToList();
        return keys.FirstOrDefault(key => key.Name.LocalName is "Timeout" or "AllowErrors") is { } stray
            ? throw new ScenarioStepException("AUS005", MarkupMessages.ScenarioNotOnStep(step.Name.LocalName, stray.Name.LocalName), stray)
            : keys;
    }

    private static string Gesture(XElement step)
    {
        var gesture = step.Attribute("Gesture").Value;
        var from = step.Attribute("From");
        var to = step.Attribute("To");
        if (gesture == "Drag" && (from == null || to == null))
        {
            throw new ScenarioStepException("AUS005", MarkupMessages.ScenarioDragNeeds(step.Name.LocalName, "From, To"), step);
        }

        if (gesture != "Drag" && (from ?? to) is { } stray)
        {
            throw new ScenarioStepException("AUS005", MarkupMessages.ScenarioOnlyForDrag(step.Name.LocalName, stray.Name.LocalName), stray);
        }

        return gesture;
    }

    private static string Target(XElement step) => Find(step.Attribute("Target"));

    private static string Focused(XElement step) => step.Attribute("Into") is { } into ? Find(into) : "session";

    private static string Find(XAttribute selector)
    {
        List<SelectorSyntax.Step> path;
        try
        {
            path = SelectorSyntax.Parse(selector.Value);
        }
        catch (FormatException e)
        {
            throw new ScenarioStepException("AUS004", MarkupMessages.ScenarioNotASelector(e.Message), selector);
        }

        var code = new StringBuilder("session");
        foreach (var step in path)
        {
            var index = step.Index is { } at ? $".At({at.ToString(CultureInfo.InvariantCulture)})" : string.Empty;
            if (step.Relation != null)
            {
                code.Append('.').Append(char.ToUpperInvariant(step.Relation[0])).Append(step.Relation.Substring(1).ToLowerInvariant()).Append("()").Append(index);
                continue;
            }

            var by = Condition(step.Conditions[0], selector);
            foreach (var condition in step.Conditions.Skip(1))
            {
                by += $".And({Condition(condition, selector)})";
            }

            code.Append(step.IsChild ? ".Child(" : ".Find(").Append(by).Append(index).Append(')');
        }

        return code.ToString();
    }

    private static string Condition((string Key, string Value) condition, XAttribute selector) => condition.Key switch
    {
        "id" => $"{Automation}By.Id({Literal(condition.Value)})",
        "name" => $"{Automation}By.Name({Literal(condition.Value)})",
        "class" => $"{Automation}By.Class({Literal(condition.Value)})",
        _ => SyntaxFacts.IsValidIdentifier(condition.Value)
            ? $"{Automation}By.Type(global::Adamantium.UI.Core.Automation.AutomationControlType.{condition.Value})"
            : throw new ScenarioStepException("AUS004", MarkupMessages.ScenarioNotASelector($"'{condition.Value}' is not a control type"), selector)
    };

    private static bool Flag(XElement step, string name) => step.Attribute(name) is { } flag && XmlConvert.ToBoolean(flag.Value);

    private static string Number(XElement step, string name, string otherwise = null) =>
        step.Attribute(name) is { } number ? Double(number.Value) : otherwise;

    private static string Pair(XElement step, string name)
    {
        var parts = step.Attribute(name).Value.Split(',');
        return $"{Double(parts[0])}, {Double(parts[1])}";
    }

    private static string Double(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);

    private static string Float(string text) =>
        float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture) + "f";

    private static (int Line, int Column) TextPosition(string text)
    {
        var parts = text.Split(':');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 1);
    }

    private static string Duration(string text) => text.EndsWith("ms", StringComparison.Ordinal)
        ? $"global::System.TimeSpan.FromMilliseconds({Double(text.Substring(0, text.Length - 2))})"
        : $"global::System.TimeSpan.FromSeconds({Double(text.Substring(0, text.Length - 1))})";

    private static string Literal(string text) => SymbolDisplay.FormatLiteral(text, true);

    private static (int Line, int Column) Where(XObject node) =>
        node is IXmlLineInfo info && info.HasLineInfo() ? (info.LineNumber, info.LinePosition) : (1, 1);
}
