using System.Collections.Generic;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Markup.Building;
using Adamantium.UI.Markup.CodeGeneration.Reflection;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The executor picks the overload C# would for the same call.</summary>
[TestFixture]
public class BuildPlanExecutorBindingTests
{
    public static class ParamsOnly
    {
        public static int Count(params object[] items) => items.Length;
    }

    [Test]
    public void AParamsOnlyMethod_CalledWithNoArgument_GetsAnEmptyArray()
    {
        var plan = new BuildPlan();
        plan.Steps.Add(new ReturnStep(new CallValue(
            new TypeValue("global::Adamantium.XamlTests.BuildPlanExecutorBindingTests.ParamsOnly", ReflectionResolvedType.Of(typeof(ParamsOnly))),
            nameof(ParamsOnly.Count))));
        var diagnostics = new List<string>();

        var result = new BuildPlanExecutor(new ReflectionTypeResolver([]), [], null, diagnostics).Run(plan, null);

        Assert.That(result, Is.EqualTo(0), string.Join(" | ", diagnostics));
        Assert.That(diagnostics, Is.Empty);
    }

    // A plan names the built-in types by their keywords, as the generator prints them; the executor knew only seven.
    [TestCase("bool", typeof(bool))]
    [TestCase("byte", typeof(byte))]
    [TestCase("sbyte", typeof(sbyte))]
    [TestCase("char", typeof(char))]
    [TestCase("decimal", typeof(decimal))]
    [TestCase("double", typeof(double))]
    [TestCase("float", typeof(float))]
    [TestCase("int", typeof(int))]
    [TestCase("uint", typeof(uint))]
    [TestCase("long", typeof(long))]
    [TestCase("ulong", typeof(ulong))]
    [TestCase("short", typeof(short))]
    [TestCase("ushort", typeof(ushort))]
    [TestCase("object", typeof(object))]
    [TestCase("string", typeof(string))]
    public void ATypeNamedByItsKeyword_IsTheBuiltInType(string keyword, System.Type expected)
    {
        var plan = new BuildPlan();
        plan.Steps.Add(new ReturnStep(new TypeOfValue(new TypeValue(keyword))));
        var diagnostics = new List<string>();

        var result = new BuildPlanExecutor(new ReflectionTypeResolver([]), [], null, diagnostics).Run(plan, null);

        Assert.That(result, Is.EqualTo(expected), string.Join(" | ", diagnostics));
    }
}
