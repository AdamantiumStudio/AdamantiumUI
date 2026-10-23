using System.Reflection;
using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Resources.Triggers;

namespace Adamantium.UI.Core.TypeParsers;

public class SelectorParser : ITypeParser<StyleSelector>
{
    private static readonly object IndexGate = new();
    private static readonly Dictionary<string, Type> TypesByName = [];
    private static readonly HashSet<Assembly> SeenAssemblies = [];
    private static readonly HashSet<string> UIAssemblyNames = [typeof(IFundamentalUIComponent).Assembly.GetName().Name];
    private static readonly List<(Assembly Assembly, string[] References)> WaitingAssemblies = [];
    private static int _seenAssemblyCount;

    public StyleSelector Parse(string value)
    {
        var splitResult = value.Split([',', ' '],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var selector = new StyleSelector();

        foreach (var rawItem in splitResult)
        {
            // Peel off any "[Prop=Value]" condition fragments first (Avalonia-style), leaving the structural part to the
            // existing type/class/id parsing. Each fragment becomes a runtime property gate on the style's setters.
            var splitItem = ExtractConditions(rawItem, selector);
            if (splitItem.Length == 0) continue;

            if (splitItem.StartsWith('#'))
            {
                selector.Id = splitItem.Substring(1);
            }
            else if (splitItem.StartsWith('.'))
            {
                if (splitItem.Contains('.')) // several chained classes  
                {
                    var group = ClassGroup.Parse(splitItem);
                    selector.ClassGroups.Add(group);
                }
                else  // single class
                {
                    selector.Classes.Add(splitItem.Substring(1));
                }
            }
            else // Control type, optionally with chained .classes: "Button" or "Button.Accent" or "Button.Accent.Big"
            {
                var parts = splitItem.Split('.', StringSplitOptions.RemoveEmptyEntries);
                var type = Resolve(parts[0]);
                if (type != null)
                {
                    selector.Types.Add(type);
                }
                for (var i = 1; i < parts.Length; i++)
                {
                    selector.Classes.Add(parts[i]);
                }
            }
        }
        return selector;
    }

    private static Type Resolve(string name)
    {
        lock (IndexGate)
        {
            IndexLoadedAssemblies();
            return TypesByName.TryGetValue(name, out var type) ? type : null;
        }
    }

    /// <summary>Takes the types of the assemblies loaded since the last time into the index selectors name types by -
    /// from any thread, so it can be done ahead of the first style.</summary>
    internal static void IndexLoadedAssemblies()
    {
        lock (IndexGate)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (assemblies.Length != _seenAssemblyCount)
            {
                IndexNewAssemblies(assemblies);
                _seenAssemblyCount = assemblies.Length;
            }
        }
    }

    private static void IndexNewAssemblies(Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            if (SeenAssemblies.Add(assembly))
            {
                WaitingAssemblies.Add((assembly, ReferencesOf(assembly)));
            }
        }

        bool indexed;
        do
        {
            indexed = false;
            for (var i = 0; i < WaitingAssemblies.Count;)
            {
                var (assembly, references) = WaitingAssemblies[i];
                if (!assembly.IsDynamic && !UIAssemblyNames.Contains(assembly.GetName().Name) &&
                    !references.Any(UIAssemblyNames.Contains))
                {
                    i++;
                    continue;
                }

                WaitingAssemblies.RemoveAt(i);
                UIAssemblyNames.Add(assembly.GetName().Name);
                Index(assembly);
                indexed = true;
            }
        }
        while (indexed);
    }

    private static string[] ReferencesOf(Assembly assembly) =>
        assembly.IsDynamic ? [] : [.. assembly.GetReferencedAssemblies().Select(reference => reference.Name)];

    private static void Index(Assembly assembly)
    {
        foreach (var type in Adamantium.Core.Reflection.LoadableTypes.Of(assembly))
        {
            if (!TypesByName.TryGetValue(type.Name, out var held))
            {
                TypesByName[type.Name] = type;
            }
            else if (!IsControl(held) && IsControl(type))
            {
                TypesByName[type.Name] = type;
            }
        }
    }

    private static bool IsControl(Type type) => typeof(IFundamentalUIComponent).IsAssignableFrom(type);

    // Splits "TabControl[TabStripPlacement=Left]" into the structural prefix ("TabControl") + one condition per bracket
    // group (added to the selector), tolerating several groups ("X[A=1][B=2]"). A group without '=' is ignored.
    private static string ExtractConditions(string item, StyleSelector selector)
    {
        var open = item.IndexOf('[');
        if (open < 0) return item;

        var structural = item[..open];
        var rest = item[open..];
        foreach (var fragment in rest.Split('[', StringSplitOptions.RemoveEmptyEntries))
        {
            var body = fragment.TrimEnd(']').Trim();
            var eq = body.IndexOf('=');
            if (eq <= 0) continue;
            selector.Conditions.Add(new Condition
            {
                Property = body[..eq].Trim(),
                Value = body[(eq + 1)..].Trim()
            });
        }
        return structural;
    }
}