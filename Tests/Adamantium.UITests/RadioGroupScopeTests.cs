using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A group name written in a control's template is that control's: two copies of the control - two canvas
/// inspectors - must not switch each other's radios.</summary>
[TestFixture]
public class RadioGroupScopeTests
{
    private static (ContentControl Control, RadioButton First, RadioButton Second) Templated()
    {
        RadioButton first = null;
        RadioButton second = null;
        var control = new ContentControl
        {
            Template = new ControlTemplate(() =>
            {
                first = new RadioButton { GroupName = "Face" };
                second = new RadioButton { GroupName = "Face" };
                var panel = new StackPanel();
                panel.Children.Add(first);
                panel.Children.Add(second);
                return new TemplateResult { RootComponent = panel };
            })
        };
        var window = new Window { Width = 200, Height = 100, Content = control };
        for (var i = 0; i < 3; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return (control, first, second);
    }

    [Test]
    public void ARadioInOneCopyOfATemplate_LeavesTheOtherCopyAlone()
    {
        var (_, firstOfA, _) = Templated();
        var (_, firstOfB, _) = Templated();
        firstOfA.IsChecked = true;

        firstOfB.IsChecked = true;

        Assert.That(firstOfA.IsChecked, Is.True, "the other copy's radio is not in this group");
    }

    [Test]
    public void ARadioInATemplate_StillClearsItsOwnSibling()
    {
        var (_, first, second) = Templated();
        first.IsChecked = true;

        second.IsChecked = true;

        Assert.That(first.IsChecked, Is.False);
    }
}
