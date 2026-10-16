using System;
using System.Collections.Generic;
using System.Globalization;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Diagnostics;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A TOGGLE'S CHECKED STATE BINDS BOTH WAYS by default, as in WPF: a check box or a tool button bound to a view
/// model's flag without a mode sets that flag when it is pressed, not only shows it - and goes on following the flag.
/// What it may not write back is left alone, and a write that fails says so instead of silencing the change.</summary>
[TestFixture]
public class ToggleButtonBindingTests
{
    private sealed class Model : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isOn;

        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value) return;
                _isOn = value;
                Raise(nameof(IsOn));
            }
        }

        public bool Locked { get; private set; }

        public void Raise(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    // The editor's pattern: one choice said as a flag per option, each setter taking only "chosen".
    private sealed class Tools : System.ComponentModel.INotifyPropertyChanged
    {
        public int Tool { get; private set; }

        public bool IsFirst
        {
            get => Tool == 1;
            set => Choose(1, value);
        }

        public bool IsSecond
        {
            get => Tool == 2;
            set => Choose(2, value);
        }

        private void Choose(int tool, bool chosen)
        {
            if (!chosen || Tool == tool) return;
            Tool = tool;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsFirst)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSecond)));
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    private sealed class Refusing : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    private static CheckBox Bound(object source, string path, IValueConverter converter = null,
        BindingMode mode = BindingMode.Default)
    {
        var box = new CheckBox { DataContext = source };
        box.SetBinding(ToggleButton.IsCheckedProperty, new Binding(path) { Converter = converter, Mode = mode });
        return box;
    }

    [Test]
    public void ACheckBoxBoundWithoutAMode_SetsTheModelWhenClicked_AndFollowsItAfter()
    {
        var model = new Model();
        var box = Bound(model, nameof(Model.IsOn));

        box.PerformClick();
        Assert.That(model.IsOn, Is.True, "the click reached the model");

        model.IsOn = false;
        BindingUpdateQueue.Flush();
        Assert.That(box.IsChecked, Is.False, "and the model still drives the box");
    }

    [Test]
    public void TwoRibbonToolsOfOneGroup_ChooseTheModelsTool()
    {
        var tools = new Tools();
        var first = new RibbonRadioButton { DataContext = tools };
        var second = new RibbonRadioButton { DataContext = tools };
        first.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(Tools.IsFirst)));
        second.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(Tools.IsSecond)));
        var group = new RibbonGroup { Items = { first, second } };

        first.PerformClick();
        BindingUpdateQueue.Flush();
        second.PerformClick();
        BindingUpdateQueue.Flush();

        Assert.That(tools.Tool, Is.EqualTo(2));
        Assert.That(first.IsChecked, Is.False);
        Assert.That(second.IsChecked, Is.True);
        GC.KeepAlive(group);
    }

    [Test]
    public void ABindingStatedOneWay_StillOnlyShowsTheModel()
    {
        var model = new Model();
        var box = new CheckBox { DataContext = model };
        box.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(Model.IsOn)) { Mode = BindingMode.OneWay });

        box.PerformClick();

        Assert.That(model.IsOn, Is.False);
    }

    // A private setter is the object's own: a click must not change what only the object may set.
    [Test]
    public void APropertyWithAPrivateSetter_IsNotWrittenBack()
    {
        var model = new Model();
        var box = Bound(model, nameof(Model.Locked), mode: BindingMode.TwoWay);

        box.PerformClick();

        Assert.That(model.Locked, Is.False);
    }

    // The same at the end of a path through a struct, which is written back up through its copies.
    [Test]
    public void APrivateSetterInsideAStruct_IsNotWrittenBack()
    {
        var model = new Holder();
        var box = Bound(model, "State.Locked", mode: BindingMode.TwoWay);

        box.PerformClick();

        Assert.That(model.State.Locked, Is.False);
    }

    private struct LockState
    {
        public bool Locked { get; private set; }
    }

    private sealed class Holder
    {
        public LockState State { get; set; }
    }

    // A converter that cannot convert back - written for showing a value only - must not take the change down with it:
    // the write runs inside the box's own change notification, and whoever listens after the binding still hears it.
    [Test]
    public void AConverterThatCannotConvertBack_DoesNotSilenceTheChange()
    {
        var model = new Model();
        var box = Bound(model, nameof(Model.IsOn), new Refusing(), BindingMode.TwoWay);
        var heard = false;
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty) heard = true;
        };
        var messages = new List<string>();
        BindingTrace.Sink = messages.Add;
        try
        {
            Assert.DoesNotThrow(() => box.PerformClick());
        }
        finally
        {
            BindingTrace.Sink = null;
        }

        Assert.That(box.IsChecked, Is.True);
        Assert.That(heard, Is.True, "a listener after the binding heard the change");
        Assert.That(model.IsOn, Is.False);
        Assert.That(messages, Has.One.Contains("IsChecked").And.Contains(nameof(NotImplementedException)),
            "and the failure is written down");
    }

    // A setter that refuses a value by throwing is written down the same way, and the change still reaches the rest.
    [Test]
    public void ASetterThatThrows_IsWrittenDown_AndDoesNotSilenceTheChange()
    {
        var model = new Throwing();
        var box = Bound(model, nameof(Throwing.IsOn));
        var heard = false;
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty) heard = true;
        };
        var messages = new List<string>();
        BindingTrace.Sink = messages.Add;
        try
        {
            Assert.DoesNotThrow(() => box.PerformClick());
        }
        finally
        {
            BindingTrace.Sink = null;
        }

        Assert.That(heard, Is.True);
        Assert.That(messages, Has.One.Contains(nameof(InvalidOperationException)));
    }

    private sealed class Throwing
    {
        public bool IsOn
        {
            get => false;
            set => throw new InvalidOperationException("refused");
        }
    }
}
