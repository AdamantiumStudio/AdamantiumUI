using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>When a two-way binding writes back: as the target changes (the default), once the target loses the focus,
/// or only when told.</summary>
[TestFixture]
public class UpdateSourceTriggerTests
{
    private sealed class Form
    {
        public string Name { get; set; } = "old";
    }

    private static (Form Form, TextBox Box, TextBox Other) Bound(UpdateSourceTrigger trigger)
    {
        var form = new Form();
        var box = new TextBox { DataContext = form };
        var other = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(other);
        box.SetBinding("Text", new Binding("Name") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = trigger });
        return (form, box, other);
    }

    [TearDown]
    public void LetGo() => FocusManager.Focus(null);

    [Test]
    public void TheDefault_WritesAsTheTargetChanges()
    {
        var (form, box, _) = Bound(UpdateSourceTrigger.Default);

        box.Text = "new";

        Assert.That(form.Name, Is.EqualTo("new"));
    }

    [Test]
    public void LostFocus_WritesOnlyOnceTheFocusLeaves()
    {
        var (form, box, other) = Bound(UpdateSourceTrigger.LostFocus);
        FocusManager.Focus(box);

        box.Text = "new";
        var whileFocused = form.Name;
        FocusManager.Focus(other);

        Assert.Multiple(() =>
        {
            Assert.That(whileFocused, Is.EqualTo("old"), "not while it is typed in");
            Assert.That(form.Name, Is.EqualTo("new"), "once the focus left");
        });
    }

    // Changed with the focus elsewhere - a clear button that takes no focus, a value set from code - there is no leaving
    // to wait for.
    [Test]
    public void LostFocus_WritesAtOnce_WhenTheTargetHasNoFocusToLose()
    {
        var (form, box, other) = Bound(UpdateSourceTrigger.LostFocus);
        FocusManager.Focus(other);

        box.Text = "new";

        Assert.That(form.Name, Is.EqualTo("new"));
    }

    [Test]
    public void Explicit_WritesOnlyWhenTold()
    {
        var (form, box, other) = Bound(UpdateSourceTrigger.Explicit);
        FocusManager.Focus(box);

        box.Text = "new";
        FocusManager.Focus(other);
        var beforeTold = form.Name;
        BindingEngine.GetBindingExpression(box, TextBox.TextProperty).UpdateSource();

        Assert.Multiple(() =>
        {
            Assert.That(beforeTold, Is.EqualTo("old"));
            Assert.That(form.Name, Is.EqualTo("new"));
        });
    }
}
