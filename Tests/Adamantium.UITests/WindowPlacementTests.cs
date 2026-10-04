using System;
using System.IO;
using Adamantium.Mathematics;
using Adamantium.UI;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class WindowPlacementTests
{
    private static readonly ScreenInfo Primary = new("DISPLAY1", new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), 1, true);
    private static readonly ScreenInfo Second = new("DISPLAY2", new Rect(1920, 0, 2560, 1440), new Rect(1920, 0, 2560, 1440), 1.5, false);

    private static WindowPlacement On(string screen, double left, double top) =>
        new() { ScreenId = screen, Left = left, Top = top, ClientWidth = 800, ClientHeight = 600 };

    [Test]
    public void AWindowGoesBackToItsScreen()
    {
        var position = WindowPlacer.Restore(On("DISPLAY2", 100, 50), [Primary, Second], new Size(800, 600));

        Assert.That(position, Is.EqualTo(new PixelPoint(2020, 50)));
    }

    [Test]
    public void AWindowFindsItsScreenWhereverTheScreenWasMoved()
    {
        var movedLeft = new ScreenInfo("DISPLAY2", new Rect(-2560, 0, 2560, 1440), new Rect(-2560, 0, 2560, 1440), 1.5, false);

        var position = WindowPlacer.Restore(On("DISPLAY2", 100, 50), [Primary, movedLeft], new Size(800, 600));

        Assert.That(position, Is.EqualTo(new PixelPoint(-2460, 50)));
    }

    [Test]
    public void AWindowWhoseScreenIsGoneHasNoPlaceToGoBackTo() =>
        Assert.That(WindowPlacer.Restore(On("DISPLAY2", 100, 50), [Primary], new Size(800, 600)), Is.Null);

    [Test]
    public void AWindowGoesBackInsideTheWorkArea()
    {
        var position = WindowPlacer.Restore(On("DISPLAY1", 1900, 1000), [Primary], new Size(800, 600));

        Assert.That(position, Is.EqualTo(new PixelPoint(1120, 440)));
    }

    [Test]
    public void CenteredAWindowStandsInTheMiddleOfTheArea() =>
        Assert.That(WindowPlacer.Center(Primary.WorkArea, new Size(800, 600)), Is.EqualTo(new PixelPoint(560, 220)));

    [Test]
    public void CenteredAWindowTallerThanTheAreaKeepsItsCaptionOnScreen() =>
        Assert.That(WindowPlacer.Center(Primary.WorkArea, new Size(800, 1200)).Y, Is.EqualTo(0));

    [Test]
    public void AWindowBelongsToTheScreenThatHoldsMostOfIt() =>
        Assert.That(WindowPlacer.ScreenOf(new Rect(1700, 100, 800, 600), [Primary, Second]), Is.SameAs(Second));

    [Test]
    public void ThePointerPicksTheScreenAndOffScreenFallsBackToThePrimary()
    {
        Assert.That(WindowPlacer.ScreenAt(new PixelPoint(2500, 300), [Primary, Second]), Is.SameAs(Second));
        Assert.That(WindowPlacer.ScreenAt(new PixelPoint(-500, 300), [Second, Primary]), Is.SameAs(Primary));
    }

    [Test]
    public void APlaceSavedIsThereForTheNextRun()
    {
        var file = Path.Combine(Path.GetTempPath(), $"placement-{Guid.NewGuid():N}", "window-placement.json");
        try
        {
            new FileWindowPlacementStore(file).Save("Main", new WindowPlacement
            {
                ScreenId = "DISPLAY2", Left = 10, Top = 20, ClientWidth = 1024, ClientHeight = 768, IsMaximized = true
            });

            var loaded = new FileWindowPlacementStore(file).Load("Main");

            Assert.That(loaded.ScreenId, Is.EqualTo("DISPLAY2"));
            Assert.That((loaded.Left, loaded.Top, loaded.ClientWidth, loaded.ClientHeight), Is.EqualTo((10d, 20d, 1024d, 768d)));
            Assert.That(loaded.IsMaximized, Is.True);
            Assert.That(new FileWindowPlacementStore(file).Load("Other"), Is.Null);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file), true);
        }
    }

    [Test]
    public void ADamagedFileForgetsThePlacesInsteadOfFailing()
    {
        var file = Path.Combine(Path.GetTempPath(), $"placement-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(file, "{ not json");

            Assert.That(new FileWindowPlacementStore(file).Load("Main"), Is.Null);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
