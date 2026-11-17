using System;
using System.ComponentModel;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// A disabled element disables everything under it, as WPF's: the fields of a disabled panel are disabled themselves - to
/// the keyboard, to automation and to the theme - and come back as they were set or bound when it is enabled. A disabled
/// panel used to block only the pointer (Pro browser's proxy settings, 17.11).
/// </summary>
[TestFixture]
public class IsEnabledFromAncestorsTests
{
    private sealed class Switch : Adamantium.Core.Commands.ICommand
    {
        private bool _can = true;

        public event EventHandler CanExecuteChanged;

        public bool Can
        {
            get => _can;
            set
            {
                _can = value;
                RaiseCanExecuteChanged();
            }
        }

        public bool CanExecute(object parameter = null) => _can;

        public void Execute(object parameter = null)
        {
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Source : INotifyPropertyChanged
    {
        private bool _allowed = true;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool Allowed
        {
            get => _allowed;
            set
            {
                _allowed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Allowed)));
            }
        }
    }

    private Window _window;

    private StackPanel InAWindow(params IMeasurableComponent[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children) panel.Children.Add(child);
        _window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { panel } } };
        Settle();
        return panel;
    }

    [TearDown]
    public void ForgetTheFocus() => FocusManager.ResetFocus();

    private void Settle()
    {
        for (var pass = 0; pass < 3; pass++) WindowExtension.UpdateTree(_window);
    }

    [Test]
    public void AFieldInADisabledPanel_IsDisabled_AndEnabledAgainWithThePanel()
    {
        var field = new TextBox();
        var panel = InAWindow(field);

        panel.IsEnabled = false;
        Assert.That(field.IsEnabled, Is.False, "the panel is disabled");
        panel.IsEnabled = true;
        Assert.That(field.IsEnabled, Is.True, "the panel is enabled again");
    }

    [Test]
    public void ThreeLevelsDown_FollowsTheTopmost()
    {
        var field = new TextBox();
        var inner = new StackPanel { Children = { new Border { Child = field } } };
        var panel = InAWindow(inner);

        panel.IsEnabled = false;
        Assert.That(inner.IsEnabled, Is.False);
        Assert.That(field.IsEnabled, Is.False);
        panel.IsEnabled = true;
        Assert.That(field.IsEnabled, Is.True);
    }

    [Test]
    public void AFieldSetToDisabled_StaysDisabled_WhenItsPanelIsEnabledAgain()
    {
        var field = new TextBox { IsEnabled = false };
        var panel = InAWindow(field);

        panel.IsEnabled = false;
        panel.IsEnabled = true;

        Assert.That(field.IsEnabled, Is.False);
    }

    [Test]
    public void AFieldBoundToEnabled_IsDisabledUnderADisabledPanel_AndKeepsItsBinding()
    {
        var source = new Source();
        var field = new TextBox { DataContext = source };
        field.SetBinding(UIComponent.IsEnabledProperty, new Binding(nameof(Source.Allowed)));
        var panel = InAWindow(field);

        panel.IsEnabled = false;
        Assert.That(field.IsEnabled, Is.False, "the panel says no");
        source.Allowed = false;
        Settle();
        panel.IsEnabled = true;
        Assert.That(field.IsEnabled, Is.False, "the binding says no");
        source.Allowed = true;
        Settle();
        Assert.That(field.IsEnabled, Is.True, "both allow - the binding was kept");
    }

    [Test]
    public void AButtonWhoseCommandCanRun_IsDisabledInADisabledPanel_AndStaysDisabledWhileTheCommandSaysNo()
    {
        var command = new Switch();
        var button = new Button { Content = "Go", Command = command };
        var panel = InAWindow(button);

        panel.IsEnabled = false;
        Assert.That(button.IsEnabled, Is.False, "the panel says no");
        command.Can = false;
        panel.IsEnabled = true;
        Assert.That(button.IsEnabled, Is.False, "the command says no");
        command.Can = true;
        Settle();
        Assert.That(button.IsEnabled, Is.True);
    }

    [Test]
    public void AFieldAddedToADisabledPanel_IsDisabled_AndEnabledOnceMovedOut()
    {
        var panel = InAWindow();
        panel.IsEnabled = false;
        var field = new TextBox();

        panel.Children.Add(field);
        Settle();
        Assert.That(field.IsEnabled, Is.False, "added to the disabled panel");

        panel.Children.Remove(field);
        ((StackPanel)_window.Content).Children.Add(field);
        Settle();
        Assert.That(field.IsEnabled, Is.True, "moved under an enabled one");
    }

    private sealed class WithAPart : Control
    {
        public WithAPart()
        {
            Part = new Button { Content = "Part" };
            Template = new ControlTemplate(() => new TemplateResult { RootComponent = new Border { Child = Part } });
        }

        public Button Part { get; }
    }

    [Test]
    public void TheTemplatePartsOfADisabledControl_AreDisabled_AndKeepTheirOwnValue()
    {
        var control = new WithAPart();
        var panel = InAWindow(control);
        Assert.That(control.Part.IsEnabled, Is.True, "built and enabled");

        panel.IsEnabled = false;
        Assert.That(control.Part.IsEnabled, Is.False, "the panel says no");
        control.Part.IsEnabled = false;
        panel.IsEnabled = true;
        Assert.That(control.Part.IsEnabled, Is.False, "the control set its part off meanwhile");
        control.Part.IsEnabled = true;
        Assert.That(control.Part.IsEnabled, Is.True);
    }

    [Test]
    public void APopupsContent_FollowsThePopupsPlace()
    {
        var inside = new Button { Content = "Inside" };
        var popup = new Popup { Child = inside };
        var panel = InAWindow(popup);

        panel.IsEnabled = false;
        Assert.That(inside.IsEnabled, Is.False);
        panel.IsEnabled = true;
        Assert.That(inside.IsEnabled, Is.True);
    }

    // Only the Span's parent changes: the button inside an inline below it must still be told.
    [Test]
    public void AButtonInASpan_MovedUnderADisabledPanel_IsDisabled_AndBackWhenMovedOut()
    {
        var button = new Button { Content = "In text" };
        var span = new Span();
        span.Inlines.Add(new InlineUIContainer { Child = button });
        var enabledText = new TextBlock();
        var disabledText = new TextBlock();
        enabledText.Inlines.Add(span);
        var panel = InAWindow(disabledText);
        ((StackPanel)_window.Content).Children.Add(enabledText);
        panel.IsEnabled = false;
        Settle();
        Assert.That(button.IsEnabled, Is.True, "under the enabled text");

        enabledText.Inlines.Remove(span);
        disabledText.Inlines.Add(span);
        Settle();
        Assert.That(button.IsEnabled, Is.False, "under the disabled panel");

        disabledText.Inlines.Remove(span);
        enabledText.Inlines.Add(span);
        Settle();
        Assert.That(button.IsEnabled, Is.True, "back under the enabled text");
    }

    [Test]
    public void AWindowOverADisabledPanel_StaysEnabled()
    {
        var inside = new Button { Content = "Cancel" };
        var window = new OverlayWindow { Content = inside };
        var panel = InAWindow(new Popup { Child = window });

        panel.IsEnabled = false;

        Assert.That(window.IsEnabled, Is.True);
        Assert.That(inside.IsEnabled, Is.True);
    }

    [Test]
    public void TheFocusedField_LosesTheFocus_WhenItsPanelIsDisabled()
    {
        var field = new TextBox();
        var panel = InAWindow(field);
        Assert.That(FocusManager.Focus(field, NavigationMethod.Tab), Is.True, "focusable while enabled");

        panel.IsEnabled = false;

        Assert.That(FocusManager.Focused is not UIComponent focused || !focused.InheritsThrough(panel), Is.True,
            "nothing in the disabled panel keeps it");
    }

    [Test]
    public void AButtonInADisabledPopup_LosesTheFocus_ThoughThePopupIsAnotherVisualTree()
    {
        var inside = new Button { Content = "Inside" };
        var popup = new Popup { Child = inside };
        var panel = InAWindow(popup);
        popup.IsOpen = true;
        Settle();
        Assert.That(FocusManager.Focus(inside, NavigationMethod.Tab), Is.True, "focusable while enabled");

        panel.IsEnabled = false;

        Assert.That(FocusManager.Focused, Is.Not.SameAs(inside));
    }

    [Test]
    public void AFieldInADisabledPanel_TakesNoFocus()
    {
        var field = new TextBox();
        var panel = InAWindow(field);
        panel.IsEnabled = false;

        FocusManager.Focus(field, NavigationMethod.Tab);

        Assert.That(FocusManager.Focused, Is.Not.SameAs(field));
    }

    [Test]
    public void AutomationSeesAFieldInADisabledPanel_AsDisabled()
    {
        var field = new TextBox();
        var panel = InAWindow(field);

        panel.IsEnabled = false;

        Assert.That(field.GetAutomationPeer().IsEnabled, Is.False);
    }
}
