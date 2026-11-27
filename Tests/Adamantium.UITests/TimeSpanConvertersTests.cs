using System;
using System.Globalization;
using Adamantium.UI.Controls.Converters;
using NUnit.Framework;

namespace Adamantium.UITests;

// A time as a slider and a player's clock show it.
[TestFixture]
public class TimeSpanConvertersTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Test]
    public void ATime_IsItsSeconds_AndBack()
    {
        var converter = new TimeSpanToSecondsConverter();

        Assert.That(converter.Convert(TimeSpan.FromSeconds(83.5), typeof(double), null, Culture), Is.EqualTo(83.5));
        Assert.That(converter.ConvertBack(83.5, typeof(TimeSpan), null, Culture), Is.EqualTo(TimeSpan.FromSeconds(83.5)));
        Assert.That(converter.Convert(null, typeof(double), null, Culture), Is.EqualTo(0.0));
        Assert.That(converter.ConvertBack(double.NaN, typeof(TimeSpan), null, Culture), Is.EqualTo(TimeSpan.Zero));
    }

    // Minutes and seconds below an hour, hours from one; a part of a second does not count.
    [TestCase(0, "00:00")]
    [TestCase(64.9, "01:04")]
    [TestCase(3599, "59:59")]
    [TestCase(3725, "1:02:05")]
    [TestCase(36000, "10:00:00")]
    public void ATime_IsAClock(double seconds, string text)
    {
        var converter = new TimeSpanToClockTextConverter();

        Assert.That(converter.Convert(TimeSpan.FromSeconds(seconds), typeof(string), null, Culture), Is.EqualTo(text));
    }

    [Test]
    public void WhatIsLeft_IsAClockBelowZero()
    {
        var converter = new TimeSpanToClockTextConverter();

        Assert.That(converter.Convert(TimeSpan.FromSeconds(87), typeof(string), "Remaining", Culture),
            Is.EqualTo("−01:27"));
    }
}
