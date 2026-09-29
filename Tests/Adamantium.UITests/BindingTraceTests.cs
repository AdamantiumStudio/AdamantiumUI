using System.Collections.Generic;
using System.ComponentModel;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What the binding trace writes down: a path that is broken, and nothing that is merely on its way to being
/// whole - a template bound before its item arrived, a named element further down the markup, a part not yet rooted.</summary>
[TestFixture]
public class BindingTraceTests
{
    private readonly List<string> _messages = [];

    private sealed class Model : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Text { get; set; } = "from the view-model";

        public void Touch() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
    }

    private sealed class Other
    {
        public string Caption { get; set; } = "other";
    }

    [SetUp]
    public void Listen()
    {
        _messages.Clear();
        BindingTrace.Sink = _messages.Add;
    }

    [TearDown]
    public void StopListening() => BindingTrace.Sink = null;

    // A name that has not been found is no source yet - not a license to read the DataContext instead.
    [Test]
    public void ANamedElementNotFoundYet_DoesNotFallBackToTheDataContext()
    {
        var target = new TextBlock { DataContext = new Model() };

        target.SetBinding("Text", new Binding("Text") { ElementName = "Missing" });
        BindingUpdateQueue.Flush();

        Assert.That(target.Text, Is.Not.EqualTo("from the view-model"));
    }

    [Test]
    public void ANameMissingFromAWindow_IsReported()
    {
        var target = new TextBlock();
        var window = new Window { Width = 200, Height = 100, Content = target };
        WindowExtension.UpdateTree(window);

        target.SetBinding("Text", new Binding("Text") { ElementName = "Missing" });
        BindingUpdateQueue.Flush();

        Assert.That(_messages, Has.Some.Contains("'Missing'"));
    }

    // Bound against the wrong object for a moment, then given the right one before the frame settled: nothing to say.
    [Test]
    public void ABreakMendedWithinTheFrame_IsNotReported()
    {
        var target = new TextBlock { DataContext = new Other() };
        target.SetBinding("Text", new Binding("Text"));

        target.DataContext = new Model();
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(_messages, Is.Empty);
            Assert.That(target.Text, Is.EqualTo("from the view-model"));
        });
    }

    [Test]
    public void ABreakThatOutlivesTheFrame_IsReportedOnce()
    {
        var target = new TextBlock { DataContext = new Other() };
        target.SetBinding("Text", new Binding("Text"));

        BindingUpdateQueue.Flush();
        BindingUpdateQueue.Flush();

        Assert.That(_messages, Has.Exactly(1).Contains("'Text'"));
    }

    // A definition in an inspector is not an element: it is in the tree when its host is, not when it merely has one.
    [Test]
    public void AnAncestorOfANonVisualPartWithAnUnrootedHost_IsNotReported()
    {
        var host = new Border();
        var definition = new NumericProperty();
        host.AddLogicalChild(definition);

        var e = new Ancestor { AncestorType = typeof(Grid), Path = "Width" }.Apply(definition, "Minimum");
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(e.Status, Is.EqualTo(BindingStatus.NotAttached));
            Assert.That(_messages, Is.Empty);
        });
    }
}
