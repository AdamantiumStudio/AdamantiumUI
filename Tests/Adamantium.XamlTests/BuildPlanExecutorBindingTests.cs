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
}
