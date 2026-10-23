using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Dispatcher;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// Window.ShowDialogAsync: the window is modal - the platform keeps the application's other windows from input - until it
// closes or is hidden, and the platform hears that before anyone hears the window closed.
[TestFixture]
public class ModalWindowTests
{
    private FakeApp _app;
    private FakeContext _context;
    private IUIApplication _previousApp;
    private IWindowPlatformService _previousPlatform;

    [SetUp]
    public void SetUp()
    {
        var resolver = new AdamantiumDependencyContainer();
        _app = new FakeApp(resolver) { Dispatcher = new InlineDispatcher() };
        _context = new FakeContext(resolver) { UIApplication = _app };
        _previousApp = UIAppContext.Current;
        _previousPlatform = UIAppContext.PlatformService;
        UIAppContext.Replace(_app, _previousPlatform);
    }

    [TearDown]
    public void TearDown() => UIAppContext.Replace(_previousApp, _previousPlatform);

    [Test]
    public void ADialog_IsModal_UntilItCloses()
    {
        var dialog = Open(owner: Open());

        var shown = dialog.ShowDialogAsync();

        Assert.That(dialog.Worker.Modal, Is.EqualTo(new[] { true }));
        Assert.That(shown.IsCompleted, Is.False);

        dialog.Close();

        Assert.That(dialog.Worker.Modal, Is.EqualTo(new[] { true, false }));
        Assert.That(dialog.IsModal, Is.False);
        Assert.That(shown.IsCompleted, Is.True);
    }

    [Test]
    public void ThePlatform_HearsTheModalStateEnd_BeforeTheDialogIsHeardClosed()
    {
        var dialog = Open(owner: Open());
        var modalWhenClosed = new List<bool>();
        dialog.Closed += (_, _) => modalWhenClosed.Add(dialog.Worker.Modal[^1]);

        _ = dialog.ShowDialogAsync();
        dialog.Close();

        Assert.That(modalWhenClosed, Is.EqualTo(new[] { false }),
            "the platform tears the window down from Closed; going while its owner refuses input hands activation away");
    }

    [Test]
    public void HidingTheDialog_EndsIt()
    {
        var dialog = Open(owner: Open());
        var shown = dialog.ShowDialogAsync();

        dialog.Hide();

        Assert.That(dialog.Worker.Modal, Is.EqualTo(new[] { true, false }));
        Assert.That(shown.IsCompleted, Is.True);
    }

    [Test]
    public void ShowingTheDialogAgain_ReturnsTheSameTask_AndMakesItModalOnce()
    {
        var dialog = Open(owner: Open());

        var first = dialog.ShowDialogAsync();
        var again = dialog.ShowDialogAsync();

        Assert.That(again, Is.SameAs(first));
        Assert.That(dialog.Worker.Modal, Is.EqualTo(new[] { true }));
    }

    [Test]
    public void ADialogClosedBeforeItIsShown_IsNeverModal_AndEndsAtOnce()
    {
        var dialog = Open(owner: Open());
        dialog.Close();

        var shown = dialog.ShowDialogAsync();

        Assert.That(shown.IsCompleted, Is.True);
        Assert.That(dialog.Worker.Modal, Is.Empty);
    }

    [Test]
    public void ASubscriberThatThrowsOnClosed_StillEndsTheDialog()
    {
        var dialog = Open(owner: Open());
        dialog.Closed += (_, _) => throw new InvalidOperationException();
        var shown = dialog.ShowDialogAsync();

        Assert.Throws<InvalidOperationException>(dialog.Close);

        Assert.That(shown.IsCompleted, Is.True);
        Assert.That(dialog.Worker.Modal, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public void ADialogWithNoOwner_IsOwnedByTheActiveWindow()
    {
        var main = Open();
        var palette = Open();
        _app.MainWindow = main;
        _app.ActiveWindow = palette;
        var dialog = Open();

        _ = dialog.ShowDialogAsync();

        Assert.That(dialog.Owner, Is.SameAs(palette));
        Assert.That(dialog.Worker.Owners, Is.EqualTo(new[] { palette }));
    }

    [Test]
    public void ADialogWithNoOwner_IsOwnedByTheMainWindow_WhenNoneIsActive()
    {
        var main = Open();
        _app.MainWindow = main;
        var dialog = Open();

        _ = dialog.ShowDialogAsync();

        Assert.That(dialog.Owner, Is.SameAs(main));
    }

    [Test]
    public void AClosedActiveWindow_IsPassedOver_ForTheMainWindow()
    {
        var main = Open();
        var gone = Open();
        _app.MainWindow = main;
        _app.ActiveWindow = gone;
        gone.Close();
        var dialog = Open();

        _ = dialog.ShowDialogAsync();

        Assert.That(dialog.Owner, Is.SameAs(main));
    }

    [Test]
    public void AnOwnerGivenBeforehand_IsKept()
    {
        var main = Open();
        _app.MainWindow = main;
        var palette = Open();
        var dialog = Open();
        dialog.Owner = palette;

        _ = dialog.ShowDialogAsync();

        Assert.That(dialog.Owner, Is.SameAs(palette));
    }

    [Test]
    public void AWindow_CannotOwnItself_NorAWindowThatOwnsIt()
    {
        var first = Open();
        var second = Open();
        second.Owner = first;

        Assert.Throws<ArgumentException>(() => first.Owner = first);
        Assert.Throws<ArgumentException>(() => first.Owner = second);
        Assert.That(first.Owner, Is.Null);
    }

    [Test]
    public void ClosingTheOwner_ClosesItsDialog_AndEndsIt()
    {
        var owner = Open();
        var dialog = Open();
        dialog.Owner = owner;
        var shown = dialog.ShowDialogAsync();

        owner.Close();

        Assert.That(dialog.IsClosed, Is.True, "an owned window goes with its owner, closed the way every window is");
        Assert.That(shown.IsCompleted, Is.True);
        Assert.That(dialog.Worker.Modal, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public void AnOwnedWindowThatRefusesToClose_StaysOpen_FreedFromItsOwner()
    {
        var owner = Open();
        var owned = Open();
        owned.Owner = owner;
        owned.Closing += (_, e) => e.Cancel = true;

        owner.Close();

        Assert.That(owned.Owner, Is.Null, "freed before the owner goes, or the platform would take it along");
        Assert.That(owned.Worker.Owners, Is.EqualTo(new IWindow[] { owner, null }));
    }

    [Test]
    public void AnOwnedWindowThatMovesToAnotherOwnerInsteadOfClosing_KeepsIt()
    {
        var owner = Open();
        var other = Open();
        var owned = Open(owner: owner);
        owned.Closing += (_, e) =>
        {
            owned.Owner = other;
            e.Cancel = true;
        };

        owner.Close();

        Assert.That(owned.Owner, Is.SameAs(other));
        Assert.That(owned.Worker.Owners, Is.EqualTo(new IWindow[] { owner, other }));
    }

    [Test]
    public void AWindowThatLeftItsOwner_StaysOpen_WhenTheOwnerCloses()
    {
        var owner = Open();
        var left = Open(owner: owner);
        left.LeaveOwner();
        left.Owner = owner;
        left.Owner = null;

        owner.Close();

        Assert.That(left.IsClosed, Is.False);
        Assert.That(left.Owner, Is.Null);
    }

    [Test]
    public void AnOwnedWindowWhoseCloseThrows_StillLetsTheOwnerClose()
    {
        var owner = Open();
        var owned = Open();
        owned.Owner = owner;
        owned.Closed += (_, _) => throw new InvalidOperationException();
        var ownerClosed = false;
        owner.Closed += (_, _) => ownerClosed = true;

        Assert.Throws<InvalidOperationException>(owner.Close);

        Assert.That(ownerClosed, Is.True);
    }

    [Test]
    public void AWindow_TakesNoOwnedWindowsOnceItIsHeardClosed()
    {
        var owner = Open();
        var late = Open();
        owner.Closed += (_, _) => late.Owner = owner;

        Assert.Throws<InvalidOperationException>(owner.Close, "nothing would close it with its owner any more");

        Assert.That(late.Owner, Is.Null);
    }

    [Test]
    public void TheOwnerOfAModalWindow_CannotChange()
    {
        var dialog = Open(owner: Open());
        _ = dialog.ShowDialogAsync();

        Assert.Throws<InvalidOperationException>(() => dialog.Owner = Open());
        Assert.Throws<InvalidOperationException>(() => dialog.Owner = null);
    }

    [Test]
    public void AClosedWindow_NeitherOwnsNorIsOwned()
    {
        var closed = Open();
        closed.Close();

        Assert.Throws<InvalidOperationException>(() => closed.Owner = Open());
        Assert.Throws<InvalidOperationException>(() => Open().Owner = closed);
    }

    [Test]
    public void AnOwnerGivenBeforeTheWindowExists_IsThePlatformsAtCreation()
    {
        var owner = Open();
        var window = new TestWindow(new RecordingWorker()) { ActivateOnShow = false, Owner = owner };

        window.AttachContextAndInitialize(_context);

        Assert.That(window.Worker.OwnerAtCreation, Is.SameAs(owner));
    }

    [Test]
    public void AModalWindowThatRefusesToCloseWithItsOwner_IsFreed_AndItsModalStateEnds()
    {
        var owner = Open();
        var dialog = Open(owner: owner);
        dialog.Closing += (_, e) => e.Cancel = true;
        var shown = dialog.ShowDialogAsync();

        owner.Close();

        Assert.That(dialog.Owner, Is.Null);
        Assert.That(dialog.IsModal, Is.False);
        Assert.That(dialog.Worker.Modal, Is.EqualTo(new[] { true, false }), "the owner is given back its input before it goes");
        Assert.That(shown.IsCompleted, Is.True);
    }

    [Test]
    public void ADialogThatRefusesToClose_IsNotClosed_AndStaysModal()
    {
        var dialog = Open(owner: Open());
        dialog.Closing += (_, e) => e.Cancel = true;
        var shown = dialog.ShowDialogAsync();

        dialog.Close();

        Assert.That(dialog.IsClosed, Is.False);
        Assert.That(dialog.IsModal, Is.True);
        Assert.That(dialog.Worker.Modal, Is.EqualTo(new[] { true }));
        Assert.That(shown.IsCompleted, Is.False);
    }

    private TestWindow Open(TestWindow owner = null)
    {
        var window = new TestWindow(new RecordingWorker()) { ActivateOnShow = false };
        window.AttachContextAndInitialize(_context);
        window.Owner = owner;
        return window;
    }

    private sealed class TestWindow(RecordingWorker worker) : Window
    {
        public RecordingWorker Worker { get; } = worker;

        protected override IWindowWorkerService CreateWindowWorker(IUIContext context) => Worker;
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsRunning => true;
        public void Run(CancellationToken token) { }
        public bool CheckAccess() => true;
        public void VerifyAccess() { }
        public Thread MainThread => Thread.CurrentThread;
        public Thread UIThread { get; set; } = Thread.CurrentThread;
        public void Invoke(Action action, DispatcherPriority priority = DispatcherPriority.Normal) => action();
        public void Invoke(Delegate action, object args) => action.DynamicInvoke(args);
        public Task InvokeAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            action();
            return Task.CompletedTask;
        }
        public Task InvokeAsync(Delegate action, object args)
        {
            action.DynamicInvoke(args);
            return Task.CompletedTask;
        }
        public void Post(Action action) => action();
    }

    private sealed class RecordingWorker : IWindowWorkerService
    {
        private IWindow _window;

        public List<bool> Modal { get; } = [];
        public List<IWindow> Owners { get; } = [];
        public IWindow OwnerAtCreation { get; private set; }

        public void SetOwner(IWindow owner) => Owners.Add(owner);
        public void UpdateModal() => Modal.Add(_window.IsModal);

        public void SetWindow(IWindow window)
        {
            _window = window;
            OwnerAtCreation = window.Owner;
            ((WindowBase)window).SetHandle(new IntPtr(1));
        }

        public void SetTitle(string title) { }
        public void SetPosition(double left, double top) { }
        public void SetSize(double clientWidth, double clientHeight) { }
        public Rect RestoreBounds => default;
        public void UpdateOverlayTraits() { }
        public void ShowWindow(WindowState windowState) { }
        public void HideWindow() { }
        public void Activate() { }
        public void RaiseWithoutActivation() { }
        public void SetMouseCapture(bool capture) { }
        public PixelPoint ScreenToClient(PixelPoint screen) => screen;
        public PixelPoint ClientToScreen(PixelPoint client) => client;
        public void SetRelativeMouseMode(bool enabled, PixelPoint restoreScreen) { }
        public void SetInputMethodEnabled(bool enabled) { }
        public void BeginMoveDrag() { }
        public IUIContext UIContext => null;
    }
}
