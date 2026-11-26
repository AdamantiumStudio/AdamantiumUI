namespace Adamantium.UI.Audio.Tests;

/// <summary>Marks told of a new language as the next layout pass tells them, without a window to lay out.</summary>
internal sealed class LanguageTold : AudioFormatBadges
{
    public void TellLanguage() => OnLanguageChanged(null);
}
