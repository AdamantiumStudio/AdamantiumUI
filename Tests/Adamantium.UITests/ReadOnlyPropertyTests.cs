using System;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A read-only property is written by the component itself, its heirs included: everyone else reads it and
/// binds from it, but every way of writing it is refused.</summary>
[TestFixture]
public class ReadOnlyPropertyTests
{
    private class Counter : FundamentalUIComponent
    {
        public static readonly AdamantiumProperty CountProperty = AdamantiumProperty.RegisterReadOnly(nameof(Count),
            typeof(int), typeof(Counter), new PropertyMetadata(0));

        public int Count => GetValue<int>(CountProperty);

        public void Add() => SetReadOnlyValue(CountProperty, Count + 1);
    }

    private sealed class Resettable : Counter
    {
        public void Reset() => SetReadOnlyValue(CountProperty, 0);
    }

    private sealed class Model
    {
        public int Count { get; set; }
    }

    private sealed class Shown : FundamentalUIComponent
    {
        public static readonly AdamantiumProperty ValueProperty = AdamantiumProperty.Register(nameof(Value),
            typeof(int), typeof(Shown), new PropertyMetadata(0));

        public int Value
        {
            get => GetValue<int>(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public void WriteAsReadOnly(int value) => SetReadOnlyValue(ValueProperty, value);
    }

    [Test]
    public void AStyleOrATriggerSetter_IsRefused_BeforeItIsRecorded()
    {
        var counter = new Counter();

        Assert.Multiple(() =>
        {
            Assert.That(() => counter.SetStyleValue(Counter.CountProperty, 3, new Style()),
                Throws.InvalidOperationException);
            Assert.That(() => counter.SetTriggerValue(Counter.CountProperty, 3, new object()),
                Throws.InvalidOperationException);
            Assert.That(counter.Count, Is.Zero);
        });
    }

    [Test]
    public void ABindingFromAReadOnlySource_DoesNotSetItBack()
    {
        var counter = new Counter();
        counter.Add();
        var shown = new Shown();
        var binding = shown.SetBinding(Shown.ValueProperty, new Binding(nameof(Counter.Count)) { Source = counter });

        Assert.Multiple(() =>
        {
            Assert.That(shown.Value, Is.EqualTo(1));
            Assert.That(binding.ResetSource(), Is.False);
            Assert.That(counter.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void TheComponent_WritesItsOwn()
    {
        var counter = new Counter();

        counter.Add();
        counter.Add();

        Assert.That(counter.Count, Is.EqualTo(2));
    }

    [Test]
    public void AnHeir_WritesWhatItInherited()
    {
        var counter = new Resettable();
        counter.Add();

        counter.Reset();

        Assert.That(counter.Count, Is.Zero);
    }

    [Test]
    public void AnyoneElse_IsRefusedEveryWayOfWriting()
    {
        var counter = new Counter();

        Assert.Multiple(() =>
        {
            Assert.That(() => counter.SetValue(Counter.CountProperty, 3), Throws.InvalidOperationException);
            Assert.That(() => counter.SetCurrentValue(Counter.CountProperty, 3), Throws.InvalidOperationException);
            Assert.That(() => counter.ClearValue(Counter.CountProperty), Throws.InvalidOperationException);
            Assert.That(() => counter.SetValue(nameof(Counter.Count), 3), Throws.InvalidOperationException);
            Assert.That(() => counter.SetBinding(Counter.CountProperty,
                new Binding(nameof(Model.Count)) { Source = new Model { Count = 3 } }), Throws.InvalidOperationException);
            Assert.That(counter.Count, Is.Zero);
        });
    }

    [Test]
    public void APropertyAnyoneWrites_IsNotWrittenAsReadOnly()
    {
        var shown = new Shown();

        Assert.That(() => shown.WriteAsReadOnly(3), Throws.InvalidOperationException);
        Assert.That(shown.Value, Is.Zero);
    }

    [Test]
    public void TheProperty_StillReachesAModel_ThroughABindingToIt()
    {
        var model = new Model();
        var counter = new Counter { DataContext = model };
        counter.SetBinding(Counter.CountProperty, new Binding(nameof(Model.Count)) { Mode = BindingMode.OneWayToSource });

        counter.Add();
        counter.Add();

        Assert.That(model.Count, Is.EqualTo(2));
    }

    private sealed class Reporter : FundamentalUIComponent
    {
        public static readonly AdamantiumProperty ErrorProperty = AdamantiumProperty.RegisterReadOnly(nameof(Error),
            typeof(string), typeof(Reporter), new PropertyMetadata(null));

        public string Error => GetValue<string>(ErrorProperty);

        public void Fail(string error) => SetReadOnlyValue(ErrorProperty, error);
    }

    private sealed class ErrorModel
    {
        public string Error { get; set; }
    }

    // Words to the model only - a read-only text the component reports through a binding to the source - are not said
    // again in a new language: the binding never writes its target, and writing a read-only one threw.
    [Test]
    public void ATextReportedToTheModel_IsNotWrittenBackInANewLanguage()
    {
        var language = Adamantium.Localization.Languages.Current;
        try
        {
            Adamantium.Localization.Languages.Current = "en";
            var model = new ErrorModel();
            var reporter = new Reporter { DataContext = model };
            reporter.SetBinding(Reporter.ErrorProperty,
                new Binding(nameof(ErrorModel.Error)) { Mode = BindingMode.OneWayToSource });
            reporter.Fail("not an archive");

            Adamantium.Localization.Languages.Current = "ru";

            Assert.Multiple(() =>
            {
                Assert.That(() => BindingUpdateQueue.Flush(), Throws.Nothing);
                Assert.That(model.Error, Is.EqualTo("not an archive"));
            });
        }
        finally
        {
            Adamantium.Localization.Languages.Current = language;
        }
    }
}
