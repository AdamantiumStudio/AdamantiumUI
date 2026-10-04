using System.ComponentModel;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Resources.Triggers;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// A binding a style's setter states is a STYLE value: it shows where the element says nothing, gives way to what the
/// element binds or its template sets, follows the element's DataContext like any binding, and goes with its style. It
/// used to take the element's own binding slot - replacing the element's binding, outranking its template, and staying
/// behind when the style left.
/// </summary>
[TestFixture]
public class StyleSetterBindingTests
{
    private static Style StyleFor<T>(params Setter[] setters)
    {
        var style = new Style();
        style.Selector.Types.Add(typeof(T));
        foreach (var setter in setters) style.Setters.Add(setter);
        return style;
    }

    [Test]
    public void TheElementsOwnBinding_OutranksTheStyles()
    {
        var words = new Words { Own = "own", Styled = "styled" };
        var button = new Button { DataContext = words };
        button.SetBinding("Content", new Binding(nameof(Words.Own)));

        StyleFor<Button>(new Setter("Content", new Binding(nameof(Words.Styled)))).Attach(button);
        Assert.That(button.Content, Is.EqualTo("own"));

        words.Own = "own, later";
        BindingUpdateQueue.Flush();
        Assert.That(button.Content, Is.EqualTo("own, later"), "and the element's binding is still alive");
    }

    [Test]
    public void TheStylesBinding_ShowsWhereTheElementSaysNothing()
    {
        var words = new Words { Styled = "styled" };
        var button = new Button { DataContext = words };

        StyleFor<Button>(new Setter("Content", new Binding(nameof(Words.Styled)))).Attach(button);
        Assert.That(button.Content, Is.EqualTo("styled"));

        words.Styled = "styled, later";
        BindingUpdateQueue.Flush();
        Assert.That(button.Content, Is.EqualTo("styled, later"));
    }

    [Test]
    public void ATemplateValue_OutranksTheStylesBinding()
    {
        var button = new Button { DataContext = new Words { Styled = "styled" } };
        button.SetValue(ContentControl.ContentProperty, "template", ValuePriority.Template);

        StyleFor<Button>(new Setter("Content", new Binding(nameof(Words.Styled)))).Attach(button);

        Assert.That(button.Content, Is.EqualTo("template"));
    }

    [Test]
    public void TheStylesBinding_GoesWithTheStyle()
    {
        var words = new Words { Styled = "styled" };
        var button = new Button { DataContext = words };
        var style = StyleFor<Button>(new Setter("Content", new Binding(nameof(Words.Styled))));
        style.Attach(button);

        style.Detach(button);
        words.Styled = "styled, later";
        BindingUpdateQueue.Flush();

        Assert.That(button.Content, Is.Null, "the style left, and its value and its binding with it");
    }

    [Test]
    public void TheStylesBinding_FollowsTheDataContext()
    {
        var button = new Button { DataContext = new Words { Styled = "first" } };
        StyleFor<Button>(new Setter("Content", new Binding(nameof(Words.Styled)))).Attach(button);

        button.DataContext = new Words { Styled = "second" };

        Assert.That(button.Content, Is.EqualTo("second"));
    }

    // What a control does to a property a style binds both ways - a toggle flipped by a click - goes back to the source,
    // and the source goes on driving the control afterwards. A current value written above the style slot would answer
    // the first and silence the second for good.
    [Test]
    public void ATwoWayStyleBinding_TakesTheControlsAnswer_AndKeepsFollowingTheSource()
    {
        var words = new Words();
        var toggle = new ToggleButton { DataContext = words };
        StyleFor<ToggleButton>(new Setter("IsChecked", new Binding(nameof(Words.Checked)) { Mode = BindingMode.TwoWay }))
            .Attach(toggle);

        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        Assert.That(words.Checked, Is.True, "the control's answer reached the source");

        words.Checked = false;
        BindingUpdateQueue.Flush();
        Assert.That(toggle.IsChecked, Is.False, "and the source still drives the control");
    }

    // A trigger's {Self} is the trigger's contribution: it shows while the trigger holds and goes when it lets go.
    [Test]
    public void ATriggersSelfBinding_GoesWithTheTrigger()
    {
        var trigger = new PropertyTrigger { Property = "IsEnabled", Value = false };
        trigger.Add(new Setter("Content", new Self { Path = "ToolTip" }));
        var style = new Style();
        style.Selector.Types.Add(typeof(Button));
        style.Triggers.Add(trigger);

        var button = new Button { ToolTip = "why not" };
        style.Attach(button);

        button.IsEnabled = false;
        Assert.That(button.Content, Is.EqualTo("why not"));

        button.IsEnabled = true;
        Assert.That(button.Content, Is.Null, "the trigger let go, and its binding with it");
    }

    private sealed class Words : INotifyPropertyChanged
    {
        private string _own;
        private string _styled;
        private bool? _checked = false;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Own
        {
            get => _own;
            set => Set(ref _own, value, nameof(Own));
        }

        public string Styled
        {
            get => _styled;
            set => Set(ref _styled, value, nameof(Styled));
        }

        public bool? Checked
        {
            get => _checked;
            set => Set(ref _checked, value, nameof(Checked));
        }

        private void Set<T>(ref T field, T value, string name)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
