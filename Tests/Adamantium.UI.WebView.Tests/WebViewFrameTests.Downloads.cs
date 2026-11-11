using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

/// <summary>
/// A file the page downloads is asked of the view and saved where the application says - its bytes as sent, followed
/// as they come; refused, nothing is saved; paused, it waits, and goes on or is cancelled.
/// </summary>
public partial class WebViewFrameTests
{
    [Test]
    public void AFileThePageDownloads_IsAskedOfTheView_AndSavedWhereSaid()
    {
        using var site = new LocalSite("<title>files</title>", fileBytes: 100_000);
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumDownloads", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(folder, "saved.bin");
        WebDownload asked = null;
        WebDownloadState state = default;
        string suggested = null;
        bool listed = false, listedWhenAsked = true;
        long total = 0, received = 0;
        try
        {
            loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
            {
                browser.Navigate(site.Origin + "/download/data.bin");
                Pump(() => view.Downloads.Count > 0, TimeSpan.FromSeconds(10));
                asked = view.Downloads.FirstOrDefault();
                if (asked == null)
                {
                    return;
                }

                suggested = asked.SuggestedName;
                listedWhenAsked = engine.Downloads.Contains(asked);
                new WebDownloadRequestedEventArgs(asked).Save(target);
                Pump(() => asked.IsFinished, TimeSpan.FromSeconds(10));
                state = asked.State;
                total = asked.TotalBytes;
                received = asked.ReceivedBytes;
                listed = engine.Downloads.Contains(asked);
            }));

            var bytes = File.Exists(target) ? File.ReadAllBytes(target) : [];
            Assert.Multiple(() =>
            {
                Assert.That(asked, Is.Not.Null, "the view was asked");
                Assert.That(suggested, Is.EqualTo("data.bin"));
                Assert.That(listedWhenAsked, Is.False, "not listed before it is saved");
                Assert.That(state, Is.EqualTo(WebDownloadState.Completed));
                Assert.That(total, Is.EqualTo(100_000));
                Assert.That(received, Is.EqualTo(100_000));
                Assert.That(listed, Is.True);
                Assert.That(asked?.FilePath, Is.EqualTo(target));
                Assert.That(asked?.Progress, Is.EqualTo(1));
                Assert.That(bytes.Length, Is.EqualTo(100_000));
                Assert.That(bytes.Select((value, i) => value == (byte)i).All(same => same), Is.True, "the bytes sent");
            });
        }
        finally
        {
            loop.Run(() => engine.ForgetDownload(asked));
            DeleteFolder(folder);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ADownloadRefused_SavesNothing(bool viewTakesIt)
    {
        using var site = new LocalSite("<title>files</title>");
        WebDownload asked = null;
        var state = WebDownloadState.Asked;
        var listed = true;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
        {
            view.TakesDownloads = viewTakesIt;
            browser.Navigate(site.Origin + "/download/refused.bin");
            Pump(() => view.Downloads.Count > 0, TimeSpan.FromSeconds(10));
            asked = view.Downloads.FirstOrDefault();
            if (asked == null)
            {
                return;
            }

            if (viewTakesIt)
            {
                new WebDownloadRequestedEventArgs(asked).Refuse();
            }

            Pump(() => false, TimeSpan.FromSeconds(1));
            state = asked.State;
            listed = engine.Downloads.Contains(asked);
        }));

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.Not.Null, "the view was asked");
            Assert.That(state, Is.EqualTo(WebDownloadState.Cancelled));
            Assert.That(listed, Is.False);
        });
    }

    [Test]
    public void ADownloadPaused_Waits_GoesOnWhenResumed_AndIsCancelled()
    {
        using var site = new LocalSite("<title>files</title>", fileBytes: 4_000_000, pieceDelayMs: 20);
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumDownloads", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(folder, "slow.bin");
        WebDownload asked = null;
        WebDownloadState paused = default, resumed = default, cancelled = default;
        long atPause = 0, afterPause = 0, afterResume = 0;
        try
        {
            loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
            {
                browser.Navigate(site.Origin + "/download/slow.bin");
                Pump(() => view.Downloads.Count > 0, TimeSpan.FromSeconds(10));
                asked = view.Downloads.FirstOrDefault();
                if (asked == null)
                {
                    return;
                }

                new WebDownloadRequestedEventArgs(asked).Save(target);
                Pump(() => asked.ReceivedBytes > 100_000, TimeSpan.FromSeconds(10));

                asked.Pause();
                Pump(() => asked.State == WebDownloadState.Paused, TimeSpan.FromSeconds(5));
                paused = asked.State;
                Pump(() => false, TimeSpan.FromMilliseconds(500));
                atPause = asked.ReceivedBytes;
                Pump(() => false, TimeSpan.FromMilliseconds(700));
                afterPause = asked.ReceivedBytes;

                asked.Resume();
                Pump(() => asked.ReceivedBytes > afterPause, TimeSpan.FromSeconds(5));
                resumed = asked.State;
                afterResume = asked.ReceivedBytes;

                asked.Cancel();
                Pump(() => asked.IsFinished, TimeSpan.FromSeconds(5));
                cancelled = asked.State;
            }));

            Assert.Multiple(() =>
            {
                Assert.That(asked, Is.Not.Null, "the view was asked");
                Assert.That(paused, Is.EqualTo(WebDownloadState.Paused));
                Assert.That(afterPause, Is.EqualTo(atPause), "nothing comes in while paused");
                Assert.That(resumed, Is.EqualTo(WebDownloadState.InProgress));
                Assert.That(afterResume, Is.GreaterThan(afterPause), "it goes on");
                Assert.That(cancelled, Is.EqualTo(WebDownloadState.Cancelled));
                Assert.That(Directory.Exists(folder) ? Directory.GetFiles(folder) : [], Is.Empty,
                    "a cancelled download leaves no file, nor the part come in");
            });
        }
        finally
        {
            loop.Run(() => engine.ForgetDownload(asked));
            DeleteFolder(folder);
        }
    }

    // A tab closed while its download comes in: the download is the profile's, not the page's.
    [Test]
    public void ADownload_OutlivesThePageThatStartedIt()
    {
        using var site = new LocalSite("<title>files</title>", fileBytes: 1_000_000, pieceDelayMs: 20);
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumDownloads", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(folder, "outlives.bin");
        WebDownload asked = null;
        var stateAfter = WebDownloadState.Asked;
        long receivedAtClose = 0;
        var keptAfterClose = 0;
        try
        {
            loop.Run(() =>
            {
                ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
                {
                    browser.Navigate(site.Origin + "/download/outlives.bin");
                    Pump(() => view.Downloads.Count > 0, TimeSpan.FromSeconds(10));
                    asked = view.Downloads.FirstOrDefault();
                    if (asked == null)
                    {
                        return;
                    }

                    new WebDownloadRequestedEventArgs(asked).Save(target);
                    Pump(() => asked.ReceivedBytes > 50_000, TimeSpan.FromSeconds(10));
                    receivedAtClose = asked.ReceivedBytes;
                });
                // ShowPage has closed the page; the download goes on, or not.
                keptAfterClose = engine.BrowserCount;
                Pump(() => asked == null || asked.IsFinished, TimeSpan.FromSeconds(30));
                stateAfter = asked?.State ?? WebDownloadState.Asked;
            });

            var left = loop.Run(() =>
            {
                Pump(() => engine.BrowserCount == 0, TimeSpan.FromSeconds(10));
                return engine.BrowserCount;
            });
            Assert.Multiple(() =>
            {
                Assert.That(receivedAtClose, Is.LessThan(1_000_000), "closed while it came in");
                Assert.That(keptAfterClose, Is.EqualTo(1), "the page was kept for it");
                Assert.That(stateAfter, Is.EqualTo(WebDownloadState.Completed));
                Assert.That(File.Exists(target) ? new FileInfo(target).Length : 0, Is.EqualTo(1_000_000));
                Assert.That(left, Is.Zero, "the page kept for the download closed once it was done");
            });
        }
        finally
        {
            loop.Run(() => engine.ForgetDownload(asked));
            DeleteFolder(folder);
        }
    }

    // A link to a file opened as a new page: the new page, shown by nobody yet, asks its opener's view.
    [Test]
    public void AFileLinkOpenedAsANewPage_IsAskedOfTheOpenersView()
    {
        using var site = new LocalSite("<title>links</title><a href='/download/linked.bin' target='_blank' " +
                                       "style='display:block;height:100vh'>file</a>");
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumDownloads", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(folder, "linked.bin");
        WebDownload asked = null;
        var state = WebDownloadState.Asked;
        var newPages = 0;
        try
        {
            loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
            {
                ClickTheMiddle(browser);
                Pump(() => view.Downloads.Count > 0, TimeSpan.FromSeconds(10));
                newPages = view.NewPages.Count;
                asked = view.Downloads.FirstOrDefault();
                if (asked != null)
                {
                    new WebDownloadRequestedEventArgs(asked).Save(target);
                    Pump(() => asked.IsFinished, TimeSpan.FromSeconds(10));
                    state = asked.State;
                }

                foreach (var (page, _) in view.NewPages)
                {
                    page.Dispose();
                }
            }));

            Assert.Multiple(() =>
            {
                Assert.That(newPages, Is.EqualTo(1), "the link opened a new page");
                Assert.That(asked, Is.Not.Null, "the opener's view was asked");
                Assert.That(state, Is.EqualTo(WebDownloadState.Completed));
                Assert.That(File.Exists(target), Is.True);
            });
        }
        finally
        {
            loop.Run(() => engine.ForgetDownload(asked));
            DeleteFolder(folder);
        }
    }

    // Closed while the application has not answered yet: an answer coming later would start a download nobody hears of.
    [Test]
    public void ADownloadAskedWhenItsPageCloses_IsRefused_AndThePageGoes()
    {
        using var site = new LocalSite("<title>files</title>");
        WebDownload asked = null;
        var listed = true;
        var lateSaveTook = true;
        var stateAfter = WebDownloadState.Asked;
        loop.Run(() =>
        {
            ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
            {
                browser.Navigate(site.Origin + "/download/unanswered.bin");
                Pump(() => view.Downloads.Count > 0, TimeSpan.FromSeconds(10));
                asked = view.Downloads.FirstOrDefault();
            });
            stateAfter = asked?.State ?? WebDownloadState.Asked;
            // Answered too late: nothing comes of it.
            if (asked != null)
            {
                lateSaveTook = new WebDownloadRequestedEventArgs(asked).Save(Path.Combine(Path.GetTempPath(), "never.bin"));
                listed = engine.Downloads.Contains(asked);
            }
        });
        var left = loop.Run(() =>
        {
            Pump(() => engine.BrowserCount == 0, TimeSpan.FromSeconds(10));
            return engine.BrowserCount;
        });

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.Not.Null, "the view was asked");
            Assert.That(stateAfter, Is.EqualTo(WebDownloadState.Cancelled));
            Assert.That(lateSaveTook, Is.False, "a late answer is told it came too late");
            Assert.That(listed, Is.False, "and saves nothing");
            Assert.That(left, Is.Zero, "the page went");
        });
    }

    [Test]
    public void ADownload_IsSavedOnce_AtAFullPath_AndForgottenOnlyWhenFinished()
    {
        using var site = new LocalSite("<title>files</title>", fileBytes: 2_000_000, pieceDelayMs: 20);
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumDownloads", Guid.NewGuid().ToString("N"));
        WebDownload asked = null;
        bool relativeThrew = false, forgottenUnfinished = true;
        string savedAt = null;
        try
        {
            loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
            {
                browser.Navigate(site.Origin + "/download/once.bin");
                Pump(() => view.Downloads.Count > 0, TimeSpan.FromSeconds(10));
                asked = view.Downloads.FirstOrDefault();
                if (asked == null)
                {
                    return;
                }

                var args = new WebDownloadRequestedEventArgs(asked);
                relativeThrew = Throws(() => args.Save("relative.bin"));
                args.Save(Path.Combine(folder, "first.bin"));
                args.Save(Path.Combine(folder, "second.bin"));
                args.Refuse();
                savedAt = asked.FilePath;
                Pump(() => asked.ReceivedBytes > 0, TimeSpan.FromSeconds(10));
                forgottenUnfinished = engine.ForgetDownload(asked);
                asked.Cancel();
                Pump(() => asked.IsFinished, TimeSpan.FromSeconds(10));
            }));

            Assert.Multiple(() =>
            {
                Assert.That(relativeThrew, Is.True, "a relative path is refused");
                Assert.That(savedAt, Is.EqualTo(Path.Combine(folder, "first.bin")), "the first answer holds");
                Assert.That(forgottenUnfinished, Is.False, "one coming in is not forgotten");
                Assert.That(asked?.State, Is.EqualTo(WebDownloadState.Cancelled));
            });
        }
        finally
        {
            loop.Run(() => engine.ForgetDownload(asked));
            DeleteFolder(folder);
        }
    }

    // The server stops short of what it said: the download is interrupted - not finished, and saying why.
    [Test]
    public void ADownloadCutShort_IsInterrupted_NotFinished()
    {
        using var site = new LocalSite("<title>files</title>", fileBytes: 1_000_000, cutAfterBytes: 200_000);
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumDownloads", Guid.NewGuid().ToString("N"));
        WebDownload asked = null;
        var state = WebDownloadState.Asked;
        var reason = WebDownloadInterruption.None;
        var finished = true;
        var forgotten = false;
        try
        {
            loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
            {
                browser.Navigate(site.Origin + "/download/cut.bin");
                Pump(() => view.Downloads.Count > 0, TimeSpan.FromSeconds(10));
                asked = view.Downloads.FirstOrDefault();
                if (asked == null)
                {
                    return;
                }

                new WebDownloadRequestedEventArgs(asked).Save(Path.Combine(folder, "cut.bin"));
                // Chromium resumes it by itself a few times first: the server sends it all again, and cuts it again.
                Pump(() => asked.State == WebDownloadState.Interrupted, TimeSpan.FromSeconds(30));
                state = asked.State;
                reason = asked.InterruptReason;
                finished = asked.IsFinished;
                // Forgotten as it finishes, as a list that drops finished downloads does.
                asked.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(WebDownload.IsFinished) && asked.IsFinished)
                    {
                        engine.ForgetDownload(asked);
                    }
                };
                asked.Cancel();
                // Long enough for Chromium to try resuming it: given up here, it is told no.
                Pump(() => false, TimeSpan.FromSeconds(3));
                forgotten = !engine.Downloads.Contains(asked);
            }));

            Assert.Multiple(() =>
            {
                Assert.That(state, Is.EqualTo(WebDownloadState.Interrupted));
                Assert.That(reason, Is.EqualTo(WebDownloadInterruption.Server), "it sent less than it said");
                Assert.That(finished, Is.False, "it may still go on");
                Assert.That(asked?.State, Is.EqualTo(WebDownloadState.Cancelled), "and cancelled, it is done");
                Assert.That(Directory.Exists(folder) ? Directory.GetFiles(folder) : [], Is.Empty, "its part with it");
                Assert.That(forgotten, Is.True);
            });
        }
        finally
        {
            loop.Run(() => engine.ForgetDownload(asked));
            DeleteFolder(folder);
        }
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        catch (IOException)
        {
        }
    }
}
