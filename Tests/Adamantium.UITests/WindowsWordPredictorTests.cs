using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Platforms.Windows;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Windows' own text prediction, asked as the keyboard asks it: words a beginning goes on to, what a misspelled
/// word was meant as, the word after the ones typed. Run where Windows has typing suggestions for the language.</summary>
[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsWordPredictorTests
{
    private WindowsWordPredictor _predictor;

    [SetUp]
    public void SetUp() => _predictor = new WindowsWordPredictor();

    [TearDown]
    public void TearDown() => _predictor.Dispose();

    [TestCase("ru-RU", "прив", "привет")]
    [TestCase("en-US", "hel", "hello")]
    public async Task ABeginning_GoesOnToItsWord(string language, string start, string word)
    {
        Assume.That(_predictor.Supports(language), Is.True, $"no typing suggestions for {language} here");

        var words = await _predictor.CompleteAsync(language, start, [], 5, CancellationToken.None);

        Assert.That(words, Does.Contain(word));
    }

    [TestCase("ru-RU", "превет", "привет")]
    [TestCase("en-US", "helo", "hello")]
    public async Task AMisspelledWord_IsCorrected(string language, string misspelled, string word)
    {
        Assume.That(_predictor.Supports(language), Is.True, $"no typing suggestions for {language} here");

        var words = await _predictor.CorrectAsync(language, misspelled, [], 5, CancellationToken.None);

        Assert.That(words, Does.Contain(word));
    }

    [TestCase("ru-RU", "добрый", "день")]
    [TestCase("en-US", "good", "morning")]
    public async Task TheWordsBefore_TellTheNextOne(string language, string before, string next)
    {
        Assume.That(_predictor.Supports(language), Is.True, $"no typing suggestions for {language} here");

        var words = await _predictor.PredictNextAsync(language, [before], 5, CancellationToken.None);

        Assert.That(words, Does.Contain(next));
    }

    [Test]
    public async Task ALanguageWindowsDoesNotKnow_GivesNoWords()
    {
        var words = await _predictor.CompleteAsync("xx-XX", "ab", [], 5, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_predictor.Supports("xx-XX"), Is.False);
            Assert.That(words, Is.Empty);
        });
    }

    // Canceled at once, the ask ends - with the words, if Windows had them first, or none - and never hangs.
    [Test]
    public void Canceled_ItEnds()
    {
        Assume.That(_predictor.Supports("en-US"), Is.True, "no typing suggestions for en-US here");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var ask = _predictor.CompleteAsync("en-US", "hel", [], 5, cancellation.Token);

        Assert.That(ask.Wait(TimeSpan.FromSeconds(5)), Is.True);
    }

    // The words before are read by Windows after the ask returns: the list the keyboard goes on changing is not the
    // one Windows reads.
    [Test]
    public async Task TheWordsBefore_ChangedAfterTheAsk_DoNotReachIt()
    {
        Assume.That(_predictor.Supports("en-US"), Is.True, "no typing suggestions for en-US here");
        var before = new List<string> { "good" };

        var ask = _predictor.PredictNextAsync("en-US", before, 5, CancellationToken.None);
        before.Clear();
        var words = await ask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(words, Does.Contain("morning"));
    }

    [Test]
    public async Task ManyAsksAtOnce_AllEnd()
    {
        Assume.That(_predictor.Supports("en-US"), Is.True, "no typing suggestions for en-US here");

        var asks = Enumerable.Range(0, 20)
            .Select(_ => _predictor.CompleteAsync("en-US", "hel", [], 3, CancellationToken.None)).ToArray();
        var all = await Task.WhenAll(asks).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(all, Has.All.Contain("hello"));
    }

    // The user's languages changing makes the generators go and come again: asks made meanwhile all end with words.
    [Test]
    public async Task AsksWhileTheUsersLanguagesChange_AllEnd()
    {
        using var languages = new TestRegistryKey();
        using var predictor = new WindowsWordPredictor(languages.Path);
        Assume.That(predictor.Supports("en-US"), Is.True, "no typing suggestions for en-US here");

        var asks = new Task<IReadOnlyList<string>>[20];
        for (var index = 0; index < asks.Length; index++)
        {
            languages.SetValue("Languages", $"en-US {index}");
            asks[index] = Task.Run(() => predictor.CompleteAsync("en-US", "hel", [], 3, CancellationToken.None));
            await Task.Delay(5);
        }

        var all = await Task.WhenAll(asks).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(all, Has.All.Contain("hello"));
            Assert.That(predictor.Supports("en-US"), Is.True);
        });
    }

    // Windows' factory is one object for the process: a predictor let go does not take it from another.
    [Test]
    public void APredictorLetGo_LeavesAnotherWorking()
    {
        Assume.That(_predictor.Supports("en-US"), Is.True, "no typing suggestions for en-US here");
        Assume.That(_predictor.Supports("ru-RU"), Is.True, "no typing suggestions for ru-RU here");
        var other = new WindowsWordPredictor();
        Assume.That(other.Supports("en-US"), Is.True);

        _predictor.Dispose();
        _predictor = new WindowsWordPredictor();

        Assert.Multiple(() =>
        {
            Assert.That(other.Supports("ru-RU"), Is.True);
            Assert.That(other.CompleteAsync("ru-RU", "прив", [], 5, CancellationToken.None).Result, Does.Contain("привет"));
        });
        other.Dispose();
    }
}
