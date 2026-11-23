using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.Core.Text;

/// <summary>The system's word prediction for the languages it knows: the words a beginning may go on to, what a
/// misspelled word may have been meant as, and the word likely to come next. A language is a BCP 47 tag; one the system
/// cannot predict in gives no words anywhere. <c>previousWords</c> are the words typed before, the nearest last.
/// </summary>
public interface IWordPredictor
{
    /// <summary>Whether the system predicts words in <paramref name="language"/>.</summary>
    bool Supports(string language);

    /// <summary>Words that begin with <paramref name="start"/>, the likeliest first.</summary>
    Task<IReadOnlyList<string>> CompleteAsync(string language, string start, IReadOnlyList<string> previousWords,
        int count, CancellationToken cancellation);

    /// <summary>What <paramref name="word"/> may have been meant as, the likeliest first.</summary>
    Task<IReadOnlyList<string>> CorrectAsync(string language, string word, IReadOnlyList<string> previousWords,
        int count, CancellationToken cancellation);

    /// <summary>The words likely to follow <paramref name="previousWords"/>, the likeliest first; none where the
    /// system does not tell.</summary>
    Task<IReadOnlyList<string>> PredictNextAsync(string language, IReadOnlyList<string> previousWords, int count,
        CancellationToken cancellation);
}
