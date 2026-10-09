using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A drop cap set on a TextBlock reaches its layout, and laying it out again follows a change.</summary>
[TestFixture]
public class TextBlockDropCapTests
{
    private static void Update(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    [Test]
    public void ByDefault_ThereIsNoDropCap()
    {
        var text = new TextBlock { Text = "Once upon a time", TextWrapping = TextWrapping.WrapByWords, Width = 200 };
        Update(new Window { Width = 600, Height = 300, Content = new Border { Child = text } });

        Assert.That(text.Layout.DropCap, Is.Null);
    }

    [Test]
    public void DropCapLines_ReachTheLayout_AndAChangeLaysItOutAgain()
    {
        var text = new TextBlock
        {
            Text = "Once upon a time there lived a king whose daughters were all beautiful.",
            TextWrapping = TextWrapping.WrapByWords,
            Width = 200,
            DropCapLines = 3,
            DropCapCharacters = 2,
        };
        var window = new Window { Width = 600, Height = 300, Content = new Border { Child = text } };
        Update(window);

        Assert.That(text.Layout.DropCap.Lines, Is.EqualTo(3));
        Assert.That(text.Layout.DropCap.Characters, Is.EqualTo(2));

        text.DropCapLines = 0;
        Update(window);

        Assert.That(text.Layout.DropCap, Is.Null);
    }
}
