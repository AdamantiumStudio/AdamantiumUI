using System.IO;
using Adamantium.Graphics.Core.EffectsFramework;
using NUnit.Framework;

namespace Adamantium.UI.GraphicsTests;

// Creates every shipped UI shader object (vkCreateShadersEXT), which otherwise happens only on a pass's first use.
[TestFixture]
public class UiShaderCreationTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    // The UI effects, which a change to a shared include can also break.
    [TestCase("ArrowEffect.fx")]
    [TestCase("InkEffect.fx")]
    [TestCase("GridEffect.fx")]
    [TestCase("BatchEffect.fx")]
    [TestCase("BrushEffect.fx")]
    public void TheUiPassesCreateOnThisDevice(string file)
    {
        var device = GpuFixture.CreateRenderDevice();

        // Compiled directly to get the error text. Logged before each attempt, so a crash leaves the failing pass as
        // the last line.
        File.AppendAllText("ui-shader-creation.log", file + " ...\r\n");

        var compiled = Adamantium.EffectsCompiler.EffectCompiler.CompileFromFile(
            Path.Combine("EffectsData", "UIFX", file));

        Assert.That(compiled.HasErrors, Is.False, Said(compiled));

        // ...and creating the shader objects is the other half.
        var effect = new Effect(device, compiled.EffectData);

        File.AppendAllText("ui-shader-creation.log", file + " created\r\n");

        Assert.That(effect.Techniques.Count, Is.GreaterThan(0));
    }

    private static string Said(Adamantium.EffectsCompiler.EffectCompilerResult compiled)
    {
        var said = new System.Text.StringBuilder(compiled.ToString());

        foreach (var message in compiled.Logger.Messages) said.AppendLine().Append(message);

        return said.ToString();
    }
}
