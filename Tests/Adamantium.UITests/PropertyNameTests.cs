using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A property is found by the name it is registered under - by a setter, a trigger, an animation, markup - and
/// its field by that name and "Property": a field registering another name is a property nothing written finds.</summary>
[TestFixture]
public class PropertyNameTests
{
    [Test]
    public void EveryPropertysField_IsNamedAsItIsRegistered()
    {
        var misnamed = new List<string>();
        foreach (var type in Library().SelectMany(TypesOf))
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                                                 BindingFlags.DeclaredOnly))
            {
                if (field.FieldType != typeof(AdamantiumProperty) || !field.IsInitOnly ||
                    field.GetValue(null) is not AdamantiumProperty property || property.OwnerType != type)
                {
                    continue;
                }

                if (field.Name != property.Name + "Property")
                {
                    misnamed.Add($"{type.Name}.{field.Name} registers '{property.Name}'");
                }
            }
        }

        Assert.That(misnamed, Is.Empty);
    }

    // Every assembly of the library these tests reach, and the ones those reach: the core, the controls, the themes.
    private static IEnumerable<Assembly> Library()
    {
        var found = new Dictionary<string, Assembly>();
        var waiting = new Stack<Assembly>();
        waiting.Push(typeof(PropertyNameTests).Assembly);
        while (waiting.Count > 0)
        {
            foreach (var reference in waiting.Pop().GetReferencedAssemblies())
            {
                if (reference.Name?.StartsWith("Adamantium.UI", StringComparison.Ordinal) == true &&
                    !found.ContainsKey(reference.Name))
                {
                    var assembly = Assembly.Load(reference);
                    found[reference.Name] = assembly;
                    waiting.Push(assembly);
                }
            }
        }

        return found.Values;
    }

    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partly)
        {
            return partly.Types.Where(type => type != null);
        }
    }
}
