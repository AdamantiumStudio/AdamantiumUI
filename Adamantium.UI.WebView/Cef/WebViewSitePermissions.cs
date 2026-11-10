using System.Text.Json;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// The answers sites were given, one per site and permission, kept in the profile's folder so they outlive the run - as
/// a browser keeps them. Chromium keeps answers of its own, but tells nobody which; these are the ones the application
/// lists and lets the user take back. On the UI loop.
/// </summary>
internal sealed class WebViewSitePermissions
{
    private const string FileName = "site-permissions.json";

    private readonly string file;
    private readonly List<WebSitePermission> answers = [];

    public WebViewSitePermissions(string profileFolder)
    {
        file = profileFolder == null ? null : Path.Combine(profileFolder, FileName);
        Load();
    }

    /// <summary>The answers, site by site.</summary>
    public IReadOnlyList<WebSitePermission> All => answers;

    /// <summary>What <paramref name="site"/> was answered for all of <paramref name="permissions"/>: true when each was
    /// allowed, false when any was blocked, null when one was never answered.</summary>
    public bool? Answer(string site, WebPermission permissions)
    {
        var allAllowed = true;
        foreach (var permission in Each(permissions))
        {
            var kept = Find(site, permission);
            if (kept == null)
            {
                allAllowed = false;
                continue;
            }

            if (!kept.IsAllowed)
            {
                return false;
            }
        }

        return allAllowed ? true : null;
    }

    /// <summary>Keeps <paramref name="allowed"/> for each of <paramref name="permissions"/> of <paramref name="site"/>.
    /// </summary>
    public void Keep(string site, WebPermission permissions, bool allowed)
    {
        // What the screen shows is asked every time, as browsers ask it: kept, a site would take it unasked.
        foreach (var permission in Each(permissions & ~WebPermission.ScreenCapture))
        {
            answers.RemoveAll(answer => answer.Site == site && answer.Permission == permission);
            answers.Add(new WebSitePermission(site, permission, allowed));
        }

        answers.Sort((a, b) => string.CompareOrdinal(a.Site, b.Site) is var bySite and not 0
            ? bySite
            : a.Permission.CompareTo(b.Permission));
        Save();
    }

    /// <summary>Takes back the answer for <paramref name="permission"/> of <paramref name="site"/>: asked again next time.
    /// </summary>
    public bool Forget(string site, WebPermission permission)
    {
        if (answers.RemoveAll(answer => answer.Site == site && answer.Permission == permission) == 0)
        {
            return false;
        }

        Save();
        return true;
    }

    /// <summary>The single permissions in <paramref name="permissions"/>.</summary>
    public static IEnumerable<WebPermission> Each(WebPermission permissions)
    {
        foreach (var permission in Enum.GetValues<WebPermission>())
        {
            if (permission != WebPermission.None && permissions.HasFlag(permission))
            {
                yield return permission;
            }
        }
    }

    private WebSitePermission Find(string site, WebPermission permission) =>
        answers.Find(answer => answer.Site == site && answer.Permission == permission);

    private void Load()
    {
        if (file == null || !File.Exists(file))
        {
            return;
        }

        try
        {
            var kept = JsonSerializer.Deserialize<List<WebViewKeptPermission>>(File.ReadAllText(file));
            foreach (var answer in kept ?? [])
            {
                // The screen is never kept: asked every time, whatever a file says.
                if (!string.IsNullOrEmpty(answer.Site) && Each(answer.Permission).Count() == 1 &&
                    answer.Permission != WebPermission.ScreenCapture)
                {
                    answers.Add(new WebSitePermission(answer.Site, answer.Permission, answer.Allowed));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Serilog.Log.Logger.Warning(exception, "The sites' permissions could not be read from {File}", file);
        }
    }

    private void Save()
    {
        if (file == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var kept = answers.Select(answer => new WebViewKeptPermission(answer.Site, answer.Permission, answer.IsAllowed))
                .ToList();
            // Whole or not at all: a file cut off midway would lose every answer.
            var writing = file + ".new";
            File.WriteAllText(writing, JsonSerializer.Serialize(kept));
            File.Move(writing, file, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Logger.Warning(exception, "The sites' permissions could not be written to {File}", file);
        }
    }
}
