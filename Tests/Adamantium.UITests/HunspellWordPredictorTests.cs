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

    // Asked on every key, from the keyboard's thread: the ask hands back at once, and the words come off that thread in
    // a frame or three - the first key of a new beginning expands its stems, the next letters only narrow them.
    [TestCase("ru-RU", "п", "пр", "при", "прив", "приве")]
    [TestCase("en-US", "h", "he", "hel", "hell", "hello")]
    public async Task EachKey_HandsBackAtOnce_AndItsWordsComeSoon(string language, params string[] starts)
    {
        var memoryBefore = System.GC.GetTotalMemory(forceFullCollection: true);
        var clock = Stopwatch.StartNew();
        Assume.That(_predictor.Supports(language), Is.True, $"no hunspell dictionary for {language} here");
        await _predictor.CompleteAsync(language, starts[0], [], 3, CancellationToken.None);
        var reading = clock.Elapsed.TotalMilliseconds;
        var held = (System.GC.GetTotalMemory(forceFullCollection: true) - memoryBefore) / (1024.0 * 1024.0);
        TestContext.Progress.WriteLine($"MEASURE {language}: the dictionary holds {held:F1} MB");

        // As typed: each beginning asked, the next letter after the one before - its words timed on that first ask. Its
        // hand-back is the best of three asks: one pause of a shared machine's scheduler spoils one, an ask that holds the
        // thread is slow in all three.
        var slowestHandBack = 0.0;
        var slowest = 0.0;
        var keys = new System.Text.StringBuilder();
        foreach (var start in starts)
        {
            var handBack = double.MaxValue;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                clock.Restart();
                var ask = _predictor.CompleteAsync(language, start, [], 3, CancellationToken.None);
                handBack = System.Math.Min(handBack, clock.Elapsed.TotalMilliseconds);
                await ask;
                if (attempt == 0)
                {
                    slowest = System.Math.Max(slowest, clock.Elapsed.TotalMilliseconds);
                    keys.Append($" {start}={clock.Elapsed.TotalMilliseconds:F1}");
                }
            }

            slowestHandBack = System.Math.Max(slowestHandBack, handBack);
        }

        TestContext.Progress.WriteLine(
            $"MEASURE {language}: read {reading:F0} ms, slowest hand-back {slowestHandBack:F2} ms, keys{keys}");
        Assert.Multiple(() =>
        {
            Assert.That(slowestHandBack, Is.LessThan(1), "the keyboard's thread is held under a millisecond");
            Assert.That(slowest, Is.LessThan(50), "a key's words within three frames");
        });
    }
}
