using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Navigation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// Unsaved work: closing panes that hold it asks ONE question for all of them - save, don't save, cancel - and a
/// document view model is marked, saved and let go through its region.
/// </summary>
[TestFixture]
public class DockingUnsavedTests
{
    private static DockingArea Area(params Pane[] documents)
    {
        var area = new DockingArea { DividerThickness = 0 };
        var group = new PaneGroup { Name = "documents", Zone = DockZone.Center };
        foreach (var pane in documents) group.Items.Add(pane);
        area.Children.Add(group);

        area.Measure(new Size(1000, 800));
        area.Arrange(new Rect(0, 0, 1000, 800));
        return area;
    }

    private static Pane Document(string id, bool dirty = false) => new() { Header = id, Id = id, IsDirty = dirty };

    // Answers for the user and counts how often it was asked, and about what.
    private static List<IReadOnlyList<string>> AnswerWith(DockingArea area, UnsavedAnswer answer)
    {
        var asked = new List<IReadOnlyList<string>>();
        area.UnsavedClosing += (_, e) =>
        {
            asked.Add(e.PaneIds);
            e.Answer = answer;
            return Task.CompletedTask;
        };
        return asked;
    }

    [Test]
    public async Task ClosingSeveral_AsksOnce_AboutTheUnsavedOnes()
    {
        var area = Area(Document("a", dirty: true), Document("b"), Document("c", dirty: true));
        var asked = AnswerWith(area, UnsavedAnswer.Discard);

        var closed = await area.CloseAllPanesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(asked, Has.Count.EqualTo(1), "one question");
            Assert.That(asked[0], Is.EqualTo(new[] { "a", "c" }));
            Assert.That(closed, Is.EqualTo(3), "don't save: they all close");
        });
    }

    [Test]
    public async Task Cancel_ClosesNothing()
    {
        var area = Area(Document("a", dirty: true), Document("b"));
        AnswerWith(area, UnsavedAnswer.Cancel);

        Assert.That(await area.CloseAllPanesAsync(), Is.EqualTo(0));
        Assert.That(area.Panes.Count(), Is.EqualTo(2));
    }

    [Test]
    public async Task Save_SavesFirst_AndAFailedSaveKeepsThemOpen()
    {
        var area = Area(Document("a", dirty: true));
        AnswerWith(area, UnsavedAnswer.Save);

        var saved = new List<string>();
        var fail = true;
        area.PanesSaving += (_, e) =>
        {
            saved.AddRange(e.PaneIds);
            e.Failed = fail;
            return Task.CompletedTask;
        };

        Assert.That(await area.ClosePaneAsync("a"), Is.False, "the save failed");
        Assert.That(area.PaneById("a"), Is.Not.Null);

        fail = false;
        Assert.That(await area.ClosePaneAsync("a"), Is.True);
        Assert.That(saved, Is.EqualTo(new[] { "a", "a" }));
    }

    [Test]
    public async Task Save_WithNobodyToSave_ClosesNothing()
    {
        var area = Area(Document("a", dirty: true));
        AnswerWith(area, UnsavedAnswer.Save);

        Assert.That(await area.ClosePaneAsync("a"), Is.False);
    }

    [Test]
    public async Task NoAnswerAndNoWindowToAskIn_ClosesNothing()
    {
        var area = Area(Document("a", dirty: true), Document("b"));

        Assert.That(await area.ClosePaneAsync("a"), Is.False, "unsaved work is not lost for want of a question");
        Assert.That(await area.ClosePaneAsync("b"), Is.True, "a saved pane closes as ever");
    }

    [Test]
    public async Task NotAsking_ClosesStraightAway()
    {
        var area = Area(Document("a", dirty: true));
        area.AsksBeforeClosingUnsaved = false;
        var asked = AnswerWith(area, UnsavedAnswer.Cancel);

        Assert.That(await area.ClosePaneAsync("a"), Is.True);
        Assert.That(asked, Is.Empty);
    }

    // --- Through a region --------------------------------------------------------------------------------------------

    private sealed class Script : INotifyPropertyChanged, IDockablePane, IDocument, INavigationAware, IDisposable
    {
        private bool _dirty;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Key { get; private set; } = "untitled";
        public int Saves { get; private set; }
        public bool Disposed { get; private set; }

        public string PaneId => Key;
        public string PaneTitle => Key;
        public DockZone PaneZone => DockZone.Center;
        public DockZone PaneAllowed => DockZone.All;

        public bool IsDirty
        {
            get => _dirty;
            set
            {
                _dirty = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
            }
        }

        public Task<bool> SaveAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            IsDirty = false;
            return Task.FromResult(true);
        }

        public Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default)
        {
            Key = context.Parameters.GetValue(DockingRegion.KeyParameter, Key);
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public bool IsNavigationTarget(NavigationContext context) => false;

        public void Dispose() => Disposed = true;
    }

    private sealed class Resolver : IDependencyResolver
    {
        public int Made { get; private set; }
        public T Resolve<T>(string name = "") => (T)Resolve(typeof(T), name);

        public object Resolve(Type type, string name = "")
        {
            Made++;
            return Activator.CreateInstance(type);
        }
    }

    private static (DockingArea Area, Adamantium.Navigation.Region Region, IDockingRegion Docking, Resolver Resolver) Docking()
    {
        var area = Area(Document("scene"));
        var resolver = new Resolver();
        var region = new Adamantium.Navigation.Region("docking", resolver, null);
        new DockingAreaRegionAdapter(new ViewLocator()).Attach(region, area);
        return (area, region, DockingRegion.Of(region), resolver);
    }

    [Test]
    public async Task ADocumentViewModel_MarksItsTab_IsSaved_AndLetGo()
    {
        var (area, _, docking, _) = Docking();
        var script = await docking.OpenDocumentAsync<Script>("main.cs");

        script.IsDirty = true;
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(area.PaneById("main.cs").IsDirty, Is.True, "the tab follows");
            Assert.That(docking.Unsaved, Is.EqualTo(new object[] { script }));
        });

        AnswerWith(area, UnsavedAnswer.Save);
        Assert.That(await docking.CloseAsync(script), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(script.Saves, Is.EqualTo(1));
            Assert.That(script.Disposed, Is.True, "a closed document is done with");
            Assert.That(docking.ViewModels, Does.Not.Contain(script));
        });
    }

    [Test]
    public async Task SaveAll_SavesEveryUnsavedOne()
    {
        var (_, _, docking, _) = Docking();
        var first = await docking.OpenDocumentAsync<Script>("a.cs");
        var second = await docking.OpenDocumentAsync<Script>("b.cs");
        var third = await docking.OpenDocumentAsync<Script>("c.cs");
        first.IsDirty = true;
        third.IsDirty = true;

        Assert.That(await docking.SaveAllAsync(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { first.Saves, second.Saves, third.Saves }, Is.EqualTo(new[] { 1, 0, 1 }));
            Assert.That(docking.Unsaved, Is.Empty);
        });
    }

    [Test]
    public async Task OpeningADocumentByKey_NeverOpensItTwice()
    {
        var (area, _, docking, resolver) = Docking();

        var main = await docking.OpenDocumentAsync<Script>("main.cs");
        await docking.OpenDocumentAsync<Script>("other.cs");
        var again = await docking.OpenDocumentAsync<Script>("main.cs");

        Assert.Multiple(() =>
        {
            Assert.That(again, Is.SameAs(main));
            Assert.That(resolver.Made, Is.EqualTo(2));
            Assert.That(docking.ActivePane, Is.SameAs(main), "brought to the front");
            Assert.That(area.PaneById("main.cs"), Is.Not.Null, "the key is the pane's id");
        });
    }
}
