using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Adamantium.UI.Automation;
using NUnit.Framework;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>A hidden window on macOS draws a still page now and then, not at the display's rate: the driver finds its layer
/// as large as what is drawn into it.</summary>
[TestFixture]
[Category("Scenario")]
[NonParallelizable]
[Platform("MacOsX")]
public class HiddenWindowIdleTests
{
    // The caret blinks and the page may animate a little; at the display's rate it would be 180.
    private const int AtMost = 30;

    [Test]
    public async Task AStillPage_IsNotDrawnAtTheDisplaysRate()
    {
        await using var session = await AutomationSession.LaunchAsync(
            Path.Combine(AppContext.BaseDirectory, "Adamantium.UI.Sandbox"),
            new LaunchOptions { FreshData = true, VirtualDisplay = true });
        await session.Find(By.Id("GalleryTabs")).Find(By.Id("Buttons")).SelectAsync();

        var asOpened = await FramesOfThreeSeconds(session);
        await session.Find(By.Id("MainWindow")).ResizeAsync(1101.3, 801.7);
        var fractional = await FramesOfThreeSeconds(session);

        Assert.Multiple(() =>
        {
            Assert.That(asOpened, Is.LessThan(AtMost), "as opened");
            Assert.That(fractional, Is.LessThan(AtMost), "sized to a part of a pixel");
        });
    }

    private static async Task<int> FramesOfThreeSeconds(AutomationSession session)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        await session.StatsAsync();
        await Task.Delay(TimeSpan.FromSeconds(3));
        var frames = Regex.Match(await session.StatsAsync(), @"over (\d+) frames");
        return frames.Success ? int.Parse(frames.Groups[1].Value) : 0;
    }
}
