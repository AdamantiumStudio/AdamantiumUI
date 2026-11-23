using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Platforms.Linux;
using Adamantium.UI.Platforms.Linux.Hunspell;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Word prediction on Linux, from the hunspell dictionaries installed and Enchant's corrections. Run where the
/// language's dictionary is installed.</summary>
[TestFixture]
[Platform("Linux")]
public class HunspellWordPredictorTests
{
    private HunspellWordPredictor _predictor;

    [SetUp]
    public void SetUp() => _predictor = new HunspellWordPredictor(new EnchantSpellChecker());

    [TestCase("ru-RU", "прив", "привет")]
    [TestCase("en-US", "hel", "hello")]
    public async Task ABeginning_GoesOnToItsWord(string language, string start, string word)
    {
        Assume.That(_predictor.Supports(language), Is.True, $"no hunspell dictionary for {language} here");

        var words = await _predictor.CompleteAsync(language, start, [], 20, CancellationToken.None);

        Assert.That(words, Does.Contain(word));
    }

    [TestCase("ru-RU", "превет", "привет")]
    [TestCase("en-US", "helo", "hello")]
    public async Task AMisspelledWord_IsCorrected(string language, string misspelled, string word)
    {
        Assume.That(new EnchantSpellChecker().Supports(language), Is.True, $"no Enchant dictionary for {language} here");

        var words = await _predictor.CorrectAsync(language, misspelled, [], 5, CancellationToken.None);

        Assert.That(words, Does.Contain(word));
    }

    [Test]
    public async Task ALanguageWithNoDictionary_IsNotSupported_AndGivesNothing()
    {
        var words = await _predictor.CompleteAsync("xx-XX", "ab", [], 5, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_predictor.Supports("xx-XX"), Is.False);
            Assert.That(words, Is.Empty);
        });
    }

    [Test]
    public async Task ThereIsNoNextWord()
    {
        var words = await _predictor.PredictNextAsync("en-US", ["good"], 3, CancellationToken.None);

        Assert.That(words, Is.Empty);
    }

    // Asked on every key: the time a beginning takes once the dictionary is read.
    [TestCase("ru-RU", "п", "пр", "при", "прив", "приве")]
    [TestCase("en-US", "h", "he", "hel", "hell", "hello")]
    public async Task EachKey_TakesLittleTime(string language, params string[] starts)
    {
        Assume.That(_predictor.Supports(language), Is.True, $"no hunspell dictionary for {language} here");
        var clock = Stopwatch.StartNew();
        await _predictor.CompleteAsync(language, starts[0], [], 3, CancellationToken.None);
        var reading = clock.Elapsed.TotalMilliseconds;

        // As typed: each beginning asked once, the next letter after the one before.
        var slowest = 0.0;
        foreach (var start in starts)
        {
            clock.Restart();
            await _predictor.CompleteAsync(language, start, [], 3, CancellationToken.None);
            slowest = System.Math.Max(slowest, clock.Elapsed.TotalMilliseconds);
        }

        TestContext.Progress.WriteLine($"MEASURE {language}: read {reading:F0} ms, slowest key {slowest:F2} ms");
        Assert.That(slowest, Is.LessThan(30), "a key's words in less than 30 ms");
    }
}
