using System.Collections.Generic;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.TypeReference;
using Adamantium.UI.Markup.Parsers;
using Microsoft.CodeAnalysis;

namespace Adamantium.UI.Generators;

/// <summary>Finds controls a test acts on - buttons, fields, lists, sliders - that have no
/// <c>AutomationProperties.AutomationId</c> or <c>x:Name</c> to be found by. Controls in templates are left out: they repeat,
/// and are found by the path to the element that stamps them.</summary>
internal static class AutomationIdCheck
{
    private static readonly string[] Interactive =
    [
        "Adamantium.UI.Controls.Primitives.ButtonBase",
        "Adamantium.UI.Controls.Text.TextBoxBase",
        "Adamantium.UI.Controls.Slider",
        "Adamantium.UI.Controls.RangeSlider",
        "Adamantium.UI.Controls.NumericUpDown",
        "Adamantium.UI.Controls.DropDown",
        "Adamantium.UI.Controls.ListBox",
        "Adamantium.UI.Controls.TabControl",
        "Adamantium.UI.Controls.TreeView",
        "Adamantium.UI.Controls.DataGrid.TreeDataGrid",
        "Adamantium.UI.Controls.Expander",
        "Adamantium.UI.Controls.ColorPicker",
        "Adamantium.UI.Controls.ColorPickerButton"
    ];

    internal readonly struct Finding(string type, int line, int position)
    {
        public string Type { get; } = type;
        public int Line { get; } = line;
        public int Position { get; } = position;
    }

    public static List<Finding> Run(AumlDocument document, Compilation compilation)
    {
        var interactive = new List<INamedTypeSymbol>();
        foreach (var name in Interactive)
        {
            if (compilation.GetTypeByMetadataName(name) is { } type)
            {
                interactive.Add(type);
            }
        }

        var findings = new List<Finding>();
        if (interactive.Count > 0)
        {
            Walk(document?.Root, compilation, interactive, findings);
        }

        return findings;
    }

    private static void Walk(IAumlAstNode node, Compilation compilation, List<INamedTypeSymbol> interactive, List<Finding> findings)
    {
        if (node is not AumlAstObjectNode obj || obj is AumlAstTemplateNode)
        {
            return;
        }

        if (IsInteractive(obj, compilation, interactive) && !HasId(obj))
        {
            findings.Add(new Finding(obj.TypeReference.Name, obj.Line, obj.Position));
        }

        foreach (var child in obj.Children)
        {
            switch (child)
            {
                case AumlAstObjectNode nested:
                    Walk(nested, compilation, interactive, findings);
                    break;
                case AumlAstPropertyNode property:
                    foreach (var value in property.Values) Walk(value, compilation, interactive, findings);
                    break;
            }
        }
    }

    private static bool IsInteractive(AumlAstObjectNode node, Compilation compilation, List<INamedTypeSymbol> interactive)
    {
        if (node.TypeReference is not AumlAstClrTypeReference { IsResolved: true } reference
            || compilation.GetTypeByMetadataName($"{reference.Namespace}.{reference.Name}") is not { } type)
        {
            return false;
        }

        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var candidate in interactive)
            {
                if (SymbolEqualityComparer.Default.Equals(current, candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasId(AumlAstObjectNode node)
    {
        foreach (var child in node.Children)
        {
            switch (child)
            {
                case AumlAstDirective { Name: "Name" }:
                case AumlAstPropertyNode { Property: AumlAstPropertyReference { Name: "AutomationId" } }:
                    return true;
            }
        }

        return false;
    }
}
