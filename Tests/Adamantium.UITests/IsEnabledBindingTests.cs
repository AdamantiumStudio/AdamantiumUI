using System;
using System.ComponentModel;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// What IsEnabled is set or bound to and what a command source's command says both count: the element is enabled only
/// when both allow it, and neither overwrites the other. A menu row's command overwrote a binding on its IsEnabled
/// (16.11), and a row bound to "no tab closed" stayed enabled.
/// </summary>
[TestFixture]
public class IsEnabledBindingTests
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

    private T InAWindow<T>(T element) where T : InputUIComponent
    {
        var stack = new StackPanel();
        stack.Children.Add(element);
        _window = new Window { Width = 200, Height = 100, Content = stack };
        Settle();
        return element;
    }

    // Bindings take a changed source on the next pass over the tree.
    private void Settle()
    {
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(_window);
    }

    [Test]
    public void AButtonBoundAndCommanded_IsEnabledOnlyWhenBothAllow_AndKeepsItsBinding()
    {
        var source = new Source();
        var command = new Switch();
        var button = InAWindow(new Button { DataContext = source, Command = command });
        button.SetBinding(UIComponent.IsEnabledProperty, new Binding(nameof(Source.Allowed)));
        Settle();

        Assert.That(button.IsEnabled, Is.True, "both allow");
        command.Can = false;
        Settle();
        Assert.That(button.IsEnabled, Is.False, "the command says no");
        command.Can = true;
        Settle();
        Assert.That(button.IsEnabled, Is.True, "the command allows again");
        source.Allowed = false;
        Settle();
        Assert.That(button.IsEnabled, Is.False, "the binding says no - it was not overwritten by the command");
        command.Can = false;
        command.Can = true;
        Settle();
        Assert.That(button.IsEnabled, Is.False, "the command's yes does not overrule the binding's no");
        source.Allowed = true;
        Settle();
        Assert.That(button.IsEnabled, Is.True);
    }

    [Test]
    public void AButtonWhoseCommandSaysNo_IsGrayedOut_ThoughIsEnabledWasNeverSet()
    {
        var button = InAWindow(new Button { Command = new Switch { Can = false } });

        Assert.That(button.IsEnabled, Is.False);
    }

    [Test]
    public void AMenuRowBoundToIsEnabled_KeepsItsBinding_WithNoCommand()
    {
        var source = new Source { Allowed = false };
        var row = InAWindow(new MenuItem { DataContext = source, Header = "Recently closed" });
        row.SetBinding(UIComponent.IsEnabledProperty, new Binding(nameof(Source.Allowed)));
        Settle();

        Assert.That(row.IsEnabled, Is.False, "bound to no");
        source.Allowed = true;
        Settle();
        Assert.That(row.IsEnabled, Is.True, "bound to yes");
    }

    [Test]
    public void AMenuRowWhoseCommandSaysNo_IsGrayedOut_AndBackWhenItSaysYes()
    {
        var command = new Switch { Can = false };
        var row = InAWindow(new MenuItem { Header = "Run", Command = command });

        Assert.That(row.IsEnabled, Is.False);
        command.Can = true;
        Settle();
        Assert.That(row.IsEnabled, Is.True);
    }
}
