using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A binding carries what its source says is wrong with the bound property onto its target element:
/// <see cref="Validation.HasErrorProperty"/>, the errors, the first of them.</summary>
[TestFixture]
public class ValidationTests
{
    private sealed class Form : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        private readonly Dictionary<string, List<string>> _errors = new();

        public string Name { get; set; } = string.Empty;

        public string Location { get; set; } = string.Empty;

        public bool HasErrors => _errors.Count > 0;

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public IEnumerable GetErrors(string propertyName) =>
            propertyName != null && _errors.TryGetValue(propertyName, out var errors) ? errors : Array.Empty<string>();

        public void Say(string property, params string[] errors)
        {
            if (errors.Length == 0)
            {
                _errors.Remove(property);
            }
            else
            {
                _errors[property] = [.. errors];
            }

            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(property));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
        }
    }

    private static (Form Form, TextBox Box) Bound(Form form = null)
    {
        form ??= new Form();
        var box = new TextBox { DataContext = form };
        box.SetBinding("Text", new Binding("Name") { Mode = BindingMode.TwoWay });
        return (form, box);
    }

    [Test]
    public void AnErrorTheSourceSays_ReachesTheBoundElement()
    {
        var (form, box) = Bound();

        form.Say(nameof(Form.Name), "Enter a name.");
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(Validation.GetHasError(box), Is.True);
            Assert.That(Validation.GetErrors(box), Is.EqualTo(new[] { "Enter a name." }));
            Assert.That(Validation.GetError(box), Is.EqualTo("Enter a name."));
        });
    }

    [Test]
    public void AnErrorTakenBack_LeavesTheElement()
    {
        var (form, box) = Bound();
        form.Say(nameof(Form.Name), "Enter a name.");
        BindingUpdateQueue.Flush();

        form.Say(nameof(Form.Name));
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(Validation.GetHasError(box), Is.False);
            Assert.That(Validation.GetErrors(box), Is.Empty);
            Assert.That(Validation.GetError(box), Is.Empty);
        });
    }

    [Test]
    public void AnErrorOfAnotherProperty_DoesNotReachIt()
    {
        var (form, box) = Bound();

        form.Say(nameof(Form.Location), "Choose a location.");
        BindingUpdateQueue.Flush();

        Assert.That(Validation.GetHasError(box), Is.False);
    }

    [Test]
    public void ASourceInErrorAlready_ShowsItAsSoonAsBound()
    {
        var form = new Form();
        form.Say(nameof(Form.Name), "Enter a name.");

        var (_, box) = Bound(form);

        Assert.That(Validation.GetHasError(box), Is.True);
    }

    [Test]
    public void TheErrorsOfTwoBindings_AreTheElementsTogether()
    {
        var (form, box) = Bound();
        box.SetBinding("Placeholder", new Binding("Location"));

        form.Say(nameof(Form.Name), "Enter a name.");
        form.Say(nameof(Form.Location), "Choose a location.");
        BindingUpdateQueue.Flush();
        form.Say(nameof(Form.Name));
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(Validation.GetHasError(box), Is.True, "the location's error is still there");
            Assert.That(Validation.GetErrors(box), Is.EqualTo(new[] { "Choose a location." }));
        });
    }

    // The errors changing as the value is typed - a check in the setter - must not put the source's value back over what
    // is typed: "1." stays "1." on its way to "1.5".
    [Test]
    public void TheErrorsChanging_LeaveTheTypedValueAlone()
    {
        var form = new Form { Name = "old" };
        var box = new TextBox { DataContext = form };
        box.SetBinding("Text", new Binding("Name") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });

        box.Text = "typed";
        form.Say(nameof(Form.Name), "Checked later.");
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("typed"));
            Assert.That(Validation.GetHasError(box), Is.True);
        });
    }

    [Test]
    public void ASourceThatSaysErrors_DoesNotHoldTheElementsBoundToIt()
    {
        var form = new Form();
        var gone = Bind(form);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(gone.IsAlive, Is.False);
        GC.KeepAlive(form);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Bind(Form form)
    {
        var box = new TextBox { DataContext = form };
        box.SetBinding("Text", new Binding("Name") { Mode = BindingMode.TwoWay });
        return new WeakReference(box);
    }

    [Test]
    public void AnotherSource_TakesTheOldOnesErrorsAway()
    {
        var (form, box) = Bound();
        form.Say(nameof(Form.Name), "Enter a name.");
        BindingUpdateQueue.Flush();

        box.DataContext = new Form();
        BindingUpdateQueue.Flush();

        Assert.That(Validation.GetHasError(box), Is.False);
    }
}
