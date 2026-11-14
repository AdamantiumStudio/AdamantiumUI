using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>CEF's times, microseconds since 1601 UTC, as a <see cref="DateTimeOffset"/> and back.</summary>
internal static class CefTimes
{
    /// <summary>The time <paramref name="time"/> is; null for none - zero, or past what a date holds.</summary>
    public static DateTimeOffset? Read(CefBasetime time) =>
        time.val > 0 && time.val < DateTime.MaxValue.ToFileTimeUtc() / 10
            ? new DateTimeOffset(DateTime.FromFileTimeUtc(time.val * 10))
            : null;

    /// <summary><paramref name="time"/> as CEF counts it.</summary>
    public static CefBasetime Write(DateTimeOffset time) => new() { val = time.UtcDateTime.ToFileTimeUtc() / 10 };
}
