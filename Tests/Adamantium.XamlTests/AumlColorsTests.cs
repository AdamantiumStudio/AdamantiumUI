using Adamantium.Mathematics;
using Adamantium.UI.LanguageServer;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The markup language server reads a color as the framework reads it.</summary>
[TestFixture]
public class AumlColorsTests
{
    [Test]
    public void TheLanguageServer_ReadsColorsAsTheFrameworkDoes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AumlColors.TryRead("#F80", out var color), Is.True);
            Assert.That(color, Is.EqualTo(Colors.Get("#FF8800")));
            Assert.That(AumlColors.TryRead("tomato", out var named) && named == Colors.Tomato, Is.True);
            Assert.That(AumlColors.TryRead("#12345", out _), Is.False, "five digits are no color to the framework");
        });
    }
}
