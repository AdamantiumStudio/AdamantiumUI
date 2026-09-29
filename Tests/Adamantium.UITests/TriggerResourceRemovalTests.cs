using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.UITests;

// A trigger painting a part via {ThemeResource}/{ObservableResource} removes its own value on exit, even while another
// trigger still owns that part property.
[TestFixture]
public class TriggerResourceRemovalTests
{
    private static readonly SolidColorBrush Checked = new(Colors.RoyalBlue);
    private static readonly SolidColorBrush Pressed = new(Colors.DarkBlue);

    // The order is the one a real click produces: the pressed trigger goes first (the button comes up), then the checked
    // one (the state flips). Removing them the other way round never showed the defect, which is why it survived.
    [Test]
    public void AThemeResourceTriggerTakesItsColorWithIt()
    {
        var part = new Border();
        var property = part.GetProperty(nameof(Border.Background));
        object checkedToken = new(), pressedToken = new();
        var resource = new ThemeResource("AccentFillColorDefault");

        resource.Apply(part, nameof(Border.Background), ValuePriority.Trigger, checkedToken);
        part.SetTriggerValue(property, Checked, checkedToken);
        resource.Apply(part, nameof(Border.Background), ValuePriority.Trigger, pressedToken);
        part.SetTriggerValue(property, Pressed, pressedToken);
        Assert.That(part.Background, Is.SameAs(Pressed), "the last trigger to apply is the one on top");

        ThemeResource.Remove(part, nameof(Border.Background), ValuePriority.Trigger, pressedToken);
        Assert.That(part.Background, Is.SameAs(Checked),
            "the pressed trigger left, so the checked one underneath it shows again");

        ThemeResource.Remove(part, nameof(Border.Background), ValuePriority.Trigger, checkedToken);
        // Not "is null": an un-triggered Border carries its own default brush. What matters is that NEITHER trigger's
        // color is still on the part.
        Assert.That(part.Background, Is.Not.SameAs(Checked).And.Not.SameAs(Pressed),
            "both triggers have left - nothing of theirs may stay on the part");
    }

    // The same seam, the same shape, the same defect: the label's Foreground is an {ObservableResource} in the very
    // triggers this bug was found in.
    [Test]
    public void AnObservableResourceTriggerTakesItsColorWithIt()
    {
        var part = new Border();
        var property = part.GetProperty(nameof(Border.Background));
        object checkedToken = new(), pressedToken = new();
        var resource = new ObservableResource("TextFillColorPrimary");

        resource.Apply(part, nameof(Border.Background), ValuePriority.Trigger, checkedToken);
        part.SetTriggerValue(property, Checked, checkedToken);
        resource.Apply(part, nameof(Border.Background), ValuePriority.Trigger, pressedToken);
        part.SetTriggerValue(property, Pressed, pressedToken);

        ObservableResource.Remove(part, nameof(Border.Background), ValuePriority.Trigger, pressedToken);
        Assert.That(part.Background, Is.SameAs(Checked),
            "the pressed trigger left, so the checked one underneath it shows again");

        ObservableResource.Remove(part, nameof(Border.Background), ValuePriority.Trigger, checkedToken);
        // Not "is null": an un-triggered Border carries its own default brush. What matters is that NEITHER trigger's
        // color is still on the part.
        Assert.That(part.Background, Is.Not.SameAs(Checked).And.Not.SameAs(Pressed),
            "both triggers have left - nothing of theirs may stay on the part");
    }
}
