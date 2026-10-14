using System.Collections;
using System.Reflection;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.MarkupExtension;
using Adamantium.UI.Markup.Building;
using Adamantium.UI.Markup.CodeGeneration;
using Adamantium.UI.Markup.CodeGeneration.Reflection;

namespace Adamantium.UI.Core.Markup;

internal sealed class BuildPlanReconciler
{
    private readonly BuildPlanExecutor _executor;
    private readonly ITypeResolver _resolver;
    private readonly List<string> _diagnostics;
    private readonly object _root;

    public BuildPlanReconciler(BuildPlanExecutor executor, ITypeResolver resolver, List<string> diagnostics, object root)
    {
        _executor = executor;
        _resolver = resolver;
        _diagnostics = diagnostics;
        _root = root;
    }

    public void Reconcile(object live, ElementStep oldElement, ElementStep newElement)
    {
        var oldProperties = PropertiesOf(oldElement);
        var newProperties = PropertiesOf(newElement);
        RemoveProperties(live, oldElement, oldProperties, newProperties);
        ReconcileChildren(live, oldElement, newElement);
        ApplyProperties(live, oldElement, newElement, oldProperties, newProperties);
    }

    private void RemoveProperties(object live, ElementStep oldElement, Dictionary<string, PropertyStep> oldProperties,
        Dictionary<string, PropertyStep> newProperties)
    {
        foreach (var (key, removed) in oldProperties)
        {
            if (!newProperties.ContainsKey(key))
            {
                Unsubscribe(removed, oldElement, live);
                ClearOwnedCollection(live, removed.Node);
                ClearLocal(live, removed.Node);
            }
        }
    }

    private void ApplyProperties(object live, ElementStep oldElement, ElementStep newElement,
        Dictionary<string, PropertyStep> oldProperties, Dictionary<string, PropertyStep> newProperties)
    {
        foreach (var (key, property) in newProperties)
        {
            oldProperties.TryGetValue(key, out var old);
            if (old != null && Signature(old.Node) == Signature(property.Node))
            {
                continue;
            }

            if (old != null)
            {
                Unsubscribe(old, oldElement, live);
            }

            ClearOwnedCollection(live, property.Node);
            if (IsLiveSource(property.Node) || (old != null && IsLiveSource(old.Node)))
            {
                ClearLocal(live, property.Node);
            }

            _executor.RunPart(property, _root, newElement.Variable, live);
        }
    }

    private void Unsubscribe(PropertyStep property, ElementStep element, object live)
    {
        foreach (var subscribe in property.Steps.OfType<SubscribeStep>())
        {
            _executor.Unsubscribe(subscribe, _root, element.Variable, live);
        }
    }

    private void ReconcileChildren(object live, ElementStep oldElement, ElementStep newElement)
    {
        var oldKids = oldElement.Steps.OfType<ChildStep>().ToList();
        var newKids = newElement.Steps.OfType<ChildStep>().ToList();

        ReconcileTree(live, Placed(oldKids, ChildPlacement.Tree), Placed(newKids, ChildPlacement.Tree), newElement);
        Rebuild(live, Placed(oldKids, ChildPlacement.MarkupCollection), Placed(newKids, ChildPlacement.MarkupCollection),
            newElement);

        ChildPlacement[] others = [ChildPlacement.Owner, ChildPlacement.Resources, ChildPlacement.LoadSlot, ChildPlacement.None];
        if (!SameNodes(oldKids.Where(k => others.Contains(k.Placement)).ToList(),
                newKids.Where(k => others.Contains(k.Placement)).ToList()))
        {
            NeedsRebuild(newElement, "children of a style, a trigger, a resource file or behind x:Load cannot change in place");
        }
    }

    private void ReconcileTree(object live, List<ChildStep> oldKids, List<ChildStep> newKids, ElementStep newElement)
    {
        if (oldKids.Count == 0 && newKids.Count == 0)
        {
            return;
        }

        if (live is not IContainer container)
        {
            Rebuild(live, oldKids, newKids, newElement);
            return;
        }

        var liveKids = container.GetChildComponents();
        var matched = MatchLive(oldKids, liveKids);
        if (matched == null)
        {
            if (!SameNodes(oldKids, newKids))
            {
                NeedsRebuild(newElement, "its children are not the ones its markup made");
            }

            return;
        }

        var oldKeys = ChildKeys(oldKids);
        var newKeys = ChildKeys(newKids);
        var byKey = new Dictionary<string, (ChildStep Step, object Instance)>(StringComparer.Ordinal);
        for (var i = 0; i < oldKids.Count; i++)
        {
            byKey[oldKeys[i]] = (oldKids[i], matched[i]);
        }

        var spliced = !oldKeys.SequenceEqual(newKeys, StringComparer.Ordinal);
        if (spliced && liveKids.Count != oldKids.Count)
        {
            NeedsRebuild(newElement, "it holds children its markup does not make (x:Load, code), so they cannot be added or moved in place");
            spliced = false;
        }

        var desired = new List<object>(newKids.Count);
        for (var i = 0; i < newKids.Count; i++)
        {
            var kid = newKids[i];
            if (byKey.TryGetValue(newKeys[i], out var match) && match.Step.Element != null && kid.Element != null)
            {
                Reconcile(match.Instance, match.Step.Element, kid.Element);
                desired.Add(match.Instance);
                continue;
            }

            if (!spliced)
            {
                continue;
            }

            var made = _executor.RunPart(kid, _root, newElement.Variable, live);
            if (kid.Element != null && made.TryGet(kid.Element.Variable, out var created) && created != null)
            {
                desired.Add(created);
            }
        }

        if (spliced)
        {
            ApplyChildOrder(container, desired);
        }
    }

    private void Rebuild(object live, List<ChildStep> oldKids, List<ChildStep> newKids, ElementStep newElement)
    {
        if (SameNodes(oldKids, newKids))
        {
            return;
        }

        var madeByMarkup = oldKids.Select(AttachedMemberOf).ToHashSet();
        foreach (var member in oldKids.Concat(newKids).Select(AttachedMemberOf).Where(m => m.Member != null).Distinct())
        {
            // Released only what the old markup put there: before it did, the member held what a style or code gave it.
            var release = madeByMarkup.Contains(member);
            var property = live.GetType().GetProperty(member.Member, BindingFlags.Public | BindingFlags.Instance);
            if (member.IsCollection && property?.GetValue(live) is IEnumerable items)
            {
                Clear(items, release);
            }
            else if (!member.IsCollection && property is { CanWrite: true })
            {
                var gone = property.GetValue(live);
                property.SetValue(live, null);
                if (release)
                {
                    Discard(gone);
                }
            }
        }

        foreach (var kid in newKids)
        {
            _executor.RunPart(kid, _root, newElement.Variable, live);
        }
    }

    private void NeedsRebuild(ElementStep element, string why)
    {
        _diagnostics.Add($"Element '{element.Node.TypeReference?.Name}' (line {element.Node.Line}): {why} - rebuild to see the change");
    }

    private static List<object> MatchLive(List<ChildStep> oldKids, IReadOnlyList<object> liveKids)
    {
        var matched = new List<object>(oldKids.Count);
        var next = 0;
        foreach (var kid in oldKids)
        {
            while (next < liveKids.Count && !IsOfType(liveKids[next], kid.Node.TypeReference?.Name))
            {
                next++;
            }

            if (next == liveKids.Count)
            {
                return null;
            }

            matched.Add(liveKids[next++]);
        }

        return matched;
    }

    private static bool IsOfType(object instance, string typeName)
    {
        for (var type = instance?.GetType(); type != null; type = type.BaseType)
        {
            if (type.Name == typeName)
            {
                return true;
            }
        }

        return false;
    }

    private static void ApplyChildOrder(IContainer container, List<object> desired)
    {
        var current = container.GetChildComponents();
        for (var i = current.Count - 1; i >= 0; i--)
        {
            if (!desired.Any(d => ReferenceEquals(d, current[i])))
            {
                var gone = current[i];
                container.RemoveChildComponentAt(i);
                Discard(gone);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            current = container.GetChildComponents();
            if (i < current.Count && ReferenceEquals(current[i], desired[i]))
            {
                continue;
            }

            for (var k = 0; k < current.Count; k++)
            {
                if (ReferenceEquals(current[k], desired[i]))
                {
                    container.RemoveChildComponentAt(k);
                    break;
                }
            }

            container.InsertChildComponent(i, desired[i]);
        }
    }

    private static bool IsLiveSource(AumlAstPropertyNode node)
    {
        return node.Values.FirstOrDefault() is AumlAstMarkupExtensionNode or AumlAstLocalizedStringNode
            or AumlAstObjectNode { TypeReference.Name: "Binding" or "MultiBinding" };
    }

    private void ClearLocal(object live, AumlAstPropertyNode node)
    {
        if (live is not AdamantiumComponent component || node.Property is not AumlAstPropertyReference reference)
        {
            return;
        }

        var property = reference.IsAttachedProperty
            ? (_resolver.Resolve(reference.OwnerType.GetFullTypeName()) as ReflectionResolvedType)?.ClrType
                .GetField($"{reference.Name}Property", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as AdamantiumProperty
            : component.GetProperty(reference.Name);
        if (property != null)
        {
            component.ClearValue(property);
        }
    }

    private static void ClearOwnedCollection(object live, AumlAstPropertyNode node)
    {
        if (node.Property is not AumlAstPropertyReference { IsAttachedProperty: false } reference)
        {
            return;
        }

        var property = live.GetType().GetProperty(reference.Name, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || property.CanWrite || property.GetValue(live) is not IEnumerable collection || collection is string)
        {
            return;
        }

        Clear(collection);
    }

    private static void Clear(IEnumerable collection, bool release = true)
    {
        var gone = release ? collection.Cast<object>().ToList() : null;
        if (collection is IList list)
        {
            list.Clear();
        }
        else
        {
            collection.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(collection, null);
        }

        foreach (var item in gone ?? [])
        {
            Discard(item);
        }
    }

    // What an edit took out and does not put back is released, as a teardown releases what it removes: left alone it
    // stays subscribed to the resources and the theme for good.
    private static void Discard(object removed)
    {
        if (removed is IUIComponent element)
        {
            DiscardedVisuals.PublishSubtree(element);
        }
    }

    private static Dictionary<string, PropertyStep> PropertiesOf(ElementStep element)
    {
        var properties = new Dictionary<string, PropertyStep>(StringComparer.Ordinal);
        foreach (var property in element.Steps.OfType<PropertyStep>())
        {
            if (property.Node.Property is AumlAstPropertyReference reference)
            {
                properties[reference.IsAttachedProperty ? $"{reference.OwnerType?.Name}.{reference.Name}" : reference.Name] = property;
            }
        }

        return properties;
    }

    private static List<ChildStep> Placed(List<ChildStep> kids, ChildPlacement placement)
    {
        return kids.Where(kid => kid.Placement == placement).ToList();
    }

    private static (string Member, bool IsCollection) AttachedMemberOf(ChildStep kid)
    {
        return kid.Steps.LastOrDefault() switch
        {
            InvokeStep { Call.Target: MemberValue collection } => (collection.Member, true),
            AssignStep assign => (assign.Member, false),
            _ => (null, false)
        };
    }

    private static bool SameNodes(List<ChildStep> oldKids, List<ChildStep> newKids)
    {
        return oldKids.Count == newKids.Count
               && oldKids.Select(k => Signature(k.Node)).SequenceEqual(newKids.Select(k => Signature(k.Node)), StringComparer.Ordinal);
    }

    private static List<string> ChildKeys(List<ChildStep> kids)
    {
        var keys = new List<string>(kids.Count);
        var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kid in kids)
        {
            var type = kid.Node.TypeReference?.Name ?? "?";
            var name = NameOf(kid.Node);
            if (!string.IsNullOrEmpty(name))
            {
                keys.Add($"{type}#{name}");
                continue;
            }

            typeCounts[type] = typeCounts.TryGetValue(type, out var count) ? count + 1 : 1;
            keys.Add($"{type}@{typeCounts[type] - 1}");
        }

        return keys;
    }

    private static string NameOf(AumlAstObjectNode node)
    {
        foreach (var child in node.Children)
        {
            if (child is AumlAstDirective { Name: AumlDirectives.Name } directive)
            {
                return (directive.Value as AumlAstTextNode)?.Text;
            }

            if (child is AumlAstPropertyNode { Property: AumlAstPropertyReference { Name: AumlDirectives.Name } } property)
            {
                return (property.Values.FirstOrDefault() as AumlAstTextNode)?.Text;
            }
        }

        return null;
    }

    private static string Signature(IAumlAstNode node)
    {
        return node switch
        {
            AumlAstPropertyNode p => "P:" + (p.Property is AumlAstPropertyReference r ? r.Name : "") + "=" +
                                     string.Join("|", p.Values.Select(Signature)),
            AumlAstTextNode t => "T:" + t.Text,
            AumlAstMarkupExtensionNode m => "M:" + (m.TypeReference?.Name ?? "") + "(" +
                                            string.Join(",", m.Arguments.Select(a => a.Name + "=" + Signature(a.Value))) + ")",
            AumlAstObjectNode o => "O:" + (o.TypeReference?.Name ?? "") + "{" + string.Join(";", o.Children.Select(Signature)) + "}",
            AumlAstDirective d => "D:" + d.Name + "=" + Signature(d.Value),
            _ => node?.ToString() ?? ""
        };
    }
}
