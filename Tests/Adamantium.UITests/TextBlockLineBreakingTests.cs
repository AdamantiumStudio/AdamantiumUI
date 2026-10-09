using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>LineBreaking set on a container reaches the text inside it, and laying it out again follows a change.</summary>
[TestFixture]
public class TextBlockLineBreakingTests
{
    private static void Update(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    [Test]
    public void ByDefault_TextBreaksALineAtATime()
    {
        var text = new TextBlock { Text = "one two three", TextWrapping = TextWrapping.WrapByWords, Width = 80 };
        Update(new Window { Width = 600, Height = 300, Content = new Border { Child = text } });

        Assert.That(text.Layout.LineBreaking, Is.EqualTo(LineBreaking.Greedy));
    }

    [Test]
    public void LineBreakingOnAContainer_ReachesTheText_AndAChangeLaysItOutAgain()
    {
        var text = new TextBlock { Text = "one two three", TextWrapping = TextWrapping.WrapByWords, Width = 80 };
        var host = new Border { Child = text, LineBreaking = LineBreaking.Paragraph };
        var window = new Window { Width = 600, Height = 300, Content = host };
        Update(window);

        Assert.That(text.Layout.LineBreaking, Is.EqualTo(LineBreaking.Paragraph));

        host.LineBreaking = LineBreaking.Greedy;
        Update(window);

        Assert.That(text.Layout.LineBreaking, Is.EqualTo(LineBreaking.Greedy));
    }
}
