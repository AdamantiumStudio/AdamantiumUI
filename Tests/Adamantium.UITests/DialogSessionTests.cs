using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Navigation;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class DialogSessionTests
{
    private sealed class Dialog : IDialogAware
    {
        public string Title => string.Empty;
        public Func<Task<bool>> Answer { get; set; } = () => Task.FromResult(true);
        public IDialogResult CloseWhileOpening { get; set; }
        public int Asked { get; private set; }

        public Task OnDialogOpenedAsync(NavigationParameters parameters, CancellationToken cancellationToken = default)
        {
            if (CloseWhileOpening != null)
            {
                Close(CloseWhileOpening);
            }
            return Task.CompletedTask;
        }

        public Task<bool> CanCloseDialogAsync()
        {
            Asked++;
            return Answer();
        }

        public event Action<IDialogResult> RequestClose;

        public void Close(IDialogResult result) => RequestClose?.Invoke(result);
    }

    [Test]
    public async Task AVetoedCloseKeepsTheDialogOpen()
    {
        var dialog = new Dialog { Answer = () => Task.FromResult(false) };
        var dismissed = 0;
        var session = await DialogSession.BeginAsync(dialog, null, () => dismissed++);

        dialog.Close(DialogResult.Ok());

        Assert.That(session.Completion.IsCompleted, Is.False);
        Assert.That(dismissed, Is.Zero);
    }

    [Test]
    public async Task ASecondCloseWhileTheAnswerIsPendingIsNotAskedAgain()
    {
        var answer = new TaskCompletionSource<bool>();
        var dialog = new Dialog { Answer = () => answer.Task };
        var dismissed = 0;
        var session = await DialogSession.BeginAsync(dialog, null, () => dismissed++);

        dialog.Close(DialogResult.Ok());
        dialog.Close(DialogResult.Cancel());
        answer.SetResult(true);
        var result = await session.Completion;

        Assert.That(dialog.Asked, Is.EqualTo(1));
        Assert.That(result.Result, Is.EqualTo(DialogButtonResult.Ok));
        Assert.That(dismissed, Is.EqualTo(1));
    }

    [Test]
    public async Task APresentationAlreadyGoneCompletesWithoutAsking()
    {
        var dialog = new Dialog { Answer = () => Task.FromResult(false) };
        var session = await DialogSession.BeginAsync(dialog, null, () => { });

        session.Close(DialogResult.Cancel());

        Assert.That(dialog.Asked, Is.Zero);
        Assert.That((await session.Completion).Result, Is.EqualTo(DialogButtonResult.Cancel));
    }

    [Test]
    public async Task ADialogThatClosedItselfWhileOpeningIsCompleteBeforeItIsShown()
    {
        var dialog = new Dialog { CloseWhileOpening = DialogResult.Cancel() };

        var session = await DialogSession.BeginAsync(dialog, null, () => { });

        Assert.That(session.Completion.IsCompleted, Is.True);
        Assert.That((await session.Completion).Result, Is.EqualTo(DialogButtonResult.Cancel));
    }
}
