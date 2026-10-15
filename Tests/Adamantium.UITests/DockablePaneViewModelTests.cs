using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adamantium.Navigation;
using NUnit.Framework;

namespace Adamantium.UITests;

// DockablePaneViewModel: the pane a view model gets by deriving from it, a tab label that follows what it is read from
// (a language table), and no hold on the view model from that source.
[TestFixture]
public class DockablePaneViewModelTests
{
    private sealed class Scene(Func<string> title, INotifyPropertyChanged titleSource = null)
        : DockablePaneViewModel(title, titleSource);

    // Stands in for a language table: it says when its strings change.
    private sealed class Strings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Language { get; private set; } = "en";

        public string Scene => Language == "ru" ? "Сцена" : "Scene";

        public void Speak(string language)
        {
            Language = language;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        public int Listeners => PropertyChanged?.GetInvocationList().Length ?? 0;
    }

    [Test]
    public void ItIsADocument_NamedByItsType_InTheDocumentArea()
    {
        var scene = new Scene(() => "Scene");

        Assert.Multiple(() =>
        {
            Assert.That(scene.PaneId, Is.EqualTo(nameof(Scene)));
            Assert.That(scene.PaneTitle, Is.EqualTo("Scene"));
            Assert.That(scene.PaneZone, Is.EqualTo(DockZone.Center));
            Assert.That(scene.PaneAllowed, Is.EqualTo(DockZone.All));
            Assert.That(scene.PaneKind, Is.EqualTo(PaneKind.Document));
        });
    }

    // The tab is bound to PaneTitle; a language switch has to tell it, or the tab keeps the old language.
    [Test]
    public void ALanguageSwitch_TellsTheTabItsTitle()
    {
        var strings = new Strings();
        var scene = new Scene(() => strings.Scene, strings);
        var told = new List<string>();
        ((INotifyPropertyChanged)scene).PropertyChanged += (_, e) => told.Add(e.PropertyName);

        strings.Speak("ru");

        Assert.Multiple(() =>
        {
            Assert.That(told, Does.Contain(nameof(DockablePaneViewModel.PaneTitle)));
            Assert.That(scene.PaneTitle, Is.EqualTo("Сцена"));
        });
    }

    // A drag writes the zone and the region listens to it.
    [Test]
    public void SettingTheZone_TellsTheRegion()
    {
        var scene = new Scene(() => "Scene");
        var told = new List<string>();
        ((INotifyPropertyChanged)scene).PropertyChanged += (_, e) => told.Add(e.PropertyName);

        scene.PaneZone = DockZone.Right;

        Assert.That(told, Does.Contain(nameof(DockablePaneViewModel.PaneZone)));
    }

    // A language table lives as long as the application; a closed pane listening to it must still be collected, and its
    // listener goes at the next change.
    [Test]
    public void TheTitleSource_DoesNotKeepAClosedPaneAlive()
    {
        var strings = new Strings();
        var pane = Make(strings);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        strings.Speak("ru");

        Assert.Multiple(() =>
        {
            Assert.That(pane.TryGetTarget(out _), Is.False);
            Assert.That(strings.Listeners, Is.Zero);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Scene> Make(Strings strings) => new(new Scene(() => strings.Scene, strings));
}
