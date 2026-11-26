using System.Linq;
using Adamantium.Localization;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// A label generated from a string keeps following the presenter's current brush, including inherited changes.
[TestFixture]
public class GeneratedLabelFollowsPresenterTests
{
    private static (ContentPresenter presenter, Window window) Hosted(Brush initial)
    {
        var presenter = new ContentPresenter { Content = "hello", Foreground = initial };
        var window = new Window { Width = 200, Height = 100, Content = presenter };
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        return (presenter, window);
    }

    private static TextBlock LabelOf(ContentPresenter presenter) =>
        presenter.VisualChildren.OfType<TextBlock>().FirstOrDefault();

    [Test]
    public void TheGeneratedLabel_TakesThePresentersBrush()
    {
        var (presenter, _) = Hosted(Brushes.Red);

        Assume.That(LabelOf(presenter), Is.Not.Null, "precondition: a string content generates a TextBlock");
        Assert.That(LabelOf(presenter).Foreground, Is.SameAs(Brushes.Red));
    }

    [Test]
    public void TheGeneratedLabel_FOLLOWSTheBrushWhenItChangesLater()
    {
        var (presenter, window) = Hosted(Brushes.Red);
        Assume.That(LabelOf(presenter)?.Foreground, Is.SameAs(Brushes.Red));

        presenter.Foreground = Brushes.Lime;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        Assert.That(LabelOf(presenter).Foreground, Is.SameAs(Brushes.Lime),
            "the label has to track the presenter, not the value it was built with");
    }

    // A number shown as content is written in the application's language, as a drop-down's box writes it.
    [Test]
    public void ANumberAsContent_IsWrittenInTheApplicationsLanguage()
    {
        var language = Languages.Current;
        try
        {
            Languages.Current = "en";
            var (presenter, window) = Hosted(Brushes.Red);
            presenter.Content = 9.81;
            WindowExtension.UpdateTree(window);
            var english = LabelOf(presenter).Text;

            Languages.Current = "ru";
            BindingUpdateQueue.Flush();

            Assert.Multiple(() =>
            {
                Assert.That(english, Is.EqualTo("9.81"));
                Assert.That(LabelOf(presenter).Text, Is.EqualTo("9,81"));
            });
        }
        finally
        {
            Languages.Current = language;
        }
    }

    private sealed class Word(string english, string russian)
    {
        public override string ToString() => Languages.Current == "ru" ? russian : english;
    }

    [Test]
    public void TheGeneratedLabel_SaysItsContentAgainInTheNewLanguage()
    {
        var language = Languages.Current;
        try
        {
            Languages.Current = "en";
            var (presenter, window) = Hosted(Brushes.Red);
            presenter.Content = new Word("Opaque", "Непрозрачный");
            WindowExtension.UpdateTree(window);
            var english = LabelOf(presenter).Text;

            Languages.Current = "ru";
            BindingUpdateQueue.Flush();

            Assert.Multiple(() =>
            {
                Assert.That(english, Is.EqualTo("Opaque"));
                Assert.That(LabelOf(presenter).Text, Is.EqualTo("Непрозрачный"));
            });
        }
        finally
        {
            Languages.Current = language;
        }
    }

    private sealed class Download : System.ComponentModel.INotifyPropertyChanged
    {
        public int Listeners { get; private set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged
        {
            add => Listeners++;
            remove => Listeners--;
        }
    }

    [Test]
    public void TheGeneratedLabel_DoesNotListenToItsContent()
    {
        var (presenter, window) = Hosted(Brushes.Red);
        var download = new Download();

        presenter.Content = download;
        WindowExtension.UpdateTree(window);

        Assert.That(download.Listeners, Is.Zero);
    }

    // One label said by a string and by things in turn, as a recycled row's is.
    [Test]
    public void TheGeneratedLabel_SaysWhateverItsPresenterHoldsNow()
    {
        var (presenter, window) = Hosted(Brushes.Red);
        var label = LabelOf(presenter);
        var said = new System.Collections.Generic.List<string>();
        foreach (var content in new object[] { new Word("one", "one"), "two", new Word("three", "three"), "four", 5 })
        {
            presenter.Content = content;
            WindowExtension.UpdateTree(window);
            said.Add(LabelOf(presenter).Text);
        }

        Assert.Multiple(() =>
        {
            Assert.That(said, Is.EqualTo(new[] { "one", "two", "three", "four", "5" }));
            Assert.That(LabelOf(presenter), Is.SameAs(label), "the label is kept");
        });
    }
}
