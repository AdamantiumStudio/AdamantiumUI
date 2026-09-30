using System.Collections.Generic;
using Adamantium.UI.Controls.Panels;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class GridLengthTests
{
    [Test]
    public void EqualLengths_HashAlike()
    {
        Assert.That(new GridLength(0, GridUnitType.Auto), Is.EqualTo(new GridLength(5, GridUnitType.Auto)));
        Assert.That(new GridLength(0, GridUnitType.Auto).GetHashCode(),
            Is.EqualTo(new GridLength(5, GridUnitType.Auto).GetHashCode()));
        Assert.That(new GridLength(2, GridUnitType.Star).GetHashCode(),
            Is.EqualTo(new GridLength(2, GridUnitType.Star).GetHashCode()));
    }

    [Test]
    public void ASetOfLengths_FindsAnEqualOne()
    {
        HashSet<GridLength> lengths = [GridLength.Auto, new(40), new(1, GridUnitType.Star)];

        Assert.That(lengths.Contains(new GridLength(7, GridUnitType.Auto)), Is.True);
        Assert.That(lengths.Contains(new GridLength(40)), Is.True);
        Assert.That(lengths.Contains(new GridLength(40, GridUnitType.Star)), Is.False);
    }
}
