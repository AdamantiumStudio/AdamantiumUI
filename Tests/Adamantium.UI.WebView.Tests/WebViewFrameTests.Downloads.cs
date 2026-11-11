using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Adamantium.UI.WebView.Cef;
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

    [Test]
    public void AFinishedDownload_IsListedByTheNextRun_WithItsPageAndTimes()
    {
        using var site = new LocalSite("<title>files</title>", fileBytes: 50_000);
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumDownloads", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(folder, "kept.bin");
        WebDownload asked = null;
        WebDownload restored = null;
        string page = null;
        DateTimeOffset? started = null, ended = null;
        string zone = null;
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        try
        {
            loop.Run(() =>
            {
                ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
                {
                    browser.Navigate(site.Origin + "/download/kept.bin");
                    Pump(() => view.Downloads.Count > 0, TimeSpan.FromSeconds(10));
                    asked = view.Downloads.FirstOrDefault();
                    if (asked == null)
                    {
                        return;
                    }

                    new WebDownloadRequestedEventArgs(asked).Save(target);
                    Pump(() => asked.IsFinished, TimeSpan.FromSeconds(10));
                    page = asked.PageAddress;
                    started = asked.StartTime;
                    ended = asked.EndTime;
                });

                // Written off the loop: the next run comes once the file says it completed.
                var written = Path.Combine(profile, "downloads.json");
                SpinWait.SpinUntil(() => ReadsCompleted(written, "kept.bin"), TimeSpan.FromSeconds(5));
                zone = OperatingSystem.IsWindows() && File.Exists(target + ":Zone.Identifier")
                    ? File.ReadAllText(target + ":Zone.Identifier")
                    : null;

                // The next run, on the same profile: the list is read before CEF starts.
                var next = new WebViewEngine(devices, null, new WebViewOptions { ProfileFolder = profile });
                try
                {
                    restored = next.Downloads.FirstOrDefault(download => download.FilePath == target);
                }
                finally
                {
                    next.Dispose();
                }
            });

            Assert.Multiple(() =>
            {
                Assert.That(page, Is.EqualTo(site.Origin + "/"), "the page it was downloaded from");
                Assert.That(started, Is.GreaterThan(before));
                Assert.That(ended, Is.GreaterThanOrEqualTo(started));
                Assert.That(restored, Is.Not.Null, "listed by the next run");
                Assert.That(restored?.State, Is.EqualTo(WebDownloadState.Completed));
                Assert.That(restored?.SuggestedName, Is.EqualTo("kept.bin"));
                Assert.That(restored?.Address, Is.EqualTo(asked?.Address));
                Assert.That(restored?.PageAddress, Is.EqualTo(page));
                Assert.That(restored?.TotalBytes, Is.EqualTo(50_000));
                Assert.That(restored?.Progress, Is.EqualTo(1));
                Assert.That(restored?.StartTime, Is.EqualTo(started));
                Assert.That(restored?.EndTime, Is.EqualTo(ended));
                if (OperatingSystem.IsWindows())
                {
                    Assert.That(zone, Does.Contain("ZoneId=3").And.Contain("HostUrl=" + asked?.Address),
                        "marked as from the internet");
                }
            });
        }
        finally
        {
            loop.Run(() => engine.ForgetDownload(asked));
            DeleteFolder(folder);
        }
    }

    [Test]
    public void ADownloadKeptComingIn_IsListedInterrupted_CannotGoOn_AndIsForgottenOrGivenUp()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumWebViewTests", Guid.NewGuid().ToString("N"));
        var part = Path.Combine(Directory.CreateDirectory(folder).FullName, "a.zip.crdownload");
        File.WriteAllText(part, "part");
        var started = new DateTimeOffset(2026, 11, 10, 14, 52, 0, TimeSpan.Zero);
        File.WriteAllText(Path.Combine(folder, "downloads.json"),
            "[" +
            "{\"Address\":\"https://example.org/a.zip\",\"Name\":\"a.zip\",\"FilePath\":\"C:\\\\a.zip\",\"PartialPath\":" +
            JsonEncoded(part) + ",\"TotalBytes\":200,\"ReceivedBytes\":50,\"State\":\"InProgress\"," +
            "\"InterruptReason\":\"None\",\"StartTime\":\"2026-11-10T14:52:00+00:00\"}," +
            "{\"Address\":\"https://example.org/b.zip\",\"Name\":\"b.zip\",\"ReceivedBytes\":9," +
            "\"State\":\"Interrupted\",\"InterruptReason\":\"Network\",\"EndTime\":\"2026-11-10T15:00:00+00:00\"}," +
            "{\"Address\":\"\",\"Name\":\"no address\",\"State\":\"Completed\"}," +
            "{\"Address\":\"https://example.org/n.bin\",\"Name\":\"a number\",\"State\":42}," +
            "{\"Address\":\"https://example.org/u.bin\",\"Name\":\"unknown\",\"State\":\"Exploded\"}," +
            "{\"Address\":\"https://example.org/c.pdf\",\"Name\":\"c.pdf\",\"TotalBytes\":10,\"ReceivedBytes\":10," +
            "\"State\":\"Completed\",\"InterruptReason\":\"None\"}" +
            "]");
        WebDownload comingIn = null, interrupted = null, completed = null;
        bool resumable = true, forgotten = false, forgottenAgain = true;
        var count = 0;
        long unknownSize = 0;
        WebDownloadState? comingInState = null;
        WebDownloadInterruption? comingInReason = null;
        DateTimeOffset? interruptedEnd = DateTimeOffset.MinValue;
        IReadOnlyList<WebViewKeptDownload> written = null;
        try
        {
            loop.Run(() =>
            {
                var next = new WebViewEngine(devices, null, new WebViewOptions { ProfileFolder = folder });
                try
                {
                    count = next.Downloads.Count;
                    comingIn = next.Downloads.FirstOrDefault(download => download.SuggestedName == "a.zip");
                    interrupted = next.Downloads.FirstOrDefault(download => download.SuggestedName == "b.zip");
                    completed = next.Downloads.FirstOrDefault(download => download.SuggestedName == "c.pdf");
                    unknownSize = interrupted?.TotalBytes ?? 0;
                    interruptedEnd = interrupted?.EndTime;
                    resumable = comingIn?.CanResume != false;
                    comingInState = comingIn?.State;
                    comingInReason = comingIn?.InterruptReason;
                    comingIn?.Resume();
                    interrupted?.Cancel();
                    forgotten = next.ForgetDownload(comingIn);
                    forgottenAgain = next.ForgetDownload(comingIn);
                }
                finally
                {
                    next.Dispose();
                }
            });

            using (var kept = new WebViewDownloadHistory(folder))
            {
                written = kept.Load();
            }

            Assert.Multiple(() =>
            {
                Assert.That(count, Is.EqualTo(3), "one without an address, or with a state not named, is left out");
                Assert.That(unknownSize, Is.EqualTo(-1), "a size not kept is not known");
                Assert.That(interruptedEnd, Is.Null, "only completing ends it");
                Assert.That(File.Exists(part), Is.False, "the part an earlier run left goes with it");
                Assert.That(comingInState, Is.EqualTo(WebDownloadState.Interrupted), "coming in when the run ended");
                Assert.That(comingInReason, Is.EqualTo(WebDownloadInterruption.ApplicationClosed));
                Assert.That(comingIn?.State, Is.EqualTo(WebDownloadState.Cancelled), "forgotten, it is given up");
                Assert.That(comingIn?.Progress, Is.EqualTo(0.25));
                Assert.That(comingIn?.StartTime, Is.EqualTo(started));
                Assert.That(resumable, Is.False, "Chromium knows nothing of it");
                Assert.That(interrupted?.State, Is.EqualTo(WebDownloadState.Cancelled), "given up");
                Assert.That(completed?.State, Is.EqualTo(WebDownloadState.Completed));
                Assert.That(completed?.Progress, Is.EqualTo(1));
                Assert.That(forgotten, Is.True, "an interrupted one kept from before can be forgotten");
                Assert.That(forgottenAgain, Is.False);
                Assert.That(written?.Select(download => download.Name), Is.EqualTo(new[] { "b.zip", "c.pdf" }));
                Assert.That(written?[0].State, Is.EqualTo(WebDownloadState.Cancelled));
            });
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Test]
    public void ADamagedListInTheProfile_ListsNothing_AndIsSetAside()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumWebViewTests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(Directory.CreateDirectory(folder).FullName, "downloads.json");
        File.WriteAllText(file, "[{\"Address\":");
        try
        {
            IReadOnlyList<WebViewKeptDownload> loaded;
            using (var history = new WebViewDownloadHistory(folder))
            {
                loaded = history.Load();
            }

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Empty);
                Assert.That(File.Exists(file), Is.False);
                Assert.That(File.ReadAllText(file + ".damaged"), Is.EqualTo("[{\"Address\":"), "kept, not written over");
            });
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Test]
    public void ASecondRunOnTheProfile_ReadsTheList_AndLeavesItBe()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumWebViewTests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(Directory.CreateDirectory(folder).FullName, "downloads.json");
        const string list = "[{\"Address\":\"https://example.org/c.pdf\",\"Name\":\"c.pdf\",\"State\":\"Completed\"}]";
        File.WriteAllText(file, list);
        var listed = 0;
        var forgotten = false;
        try
        {
            using (var first = new WebViewDownloadHistory(folder))
            {
                loop.Run(() =>
                {
                    var second = new WebViewEngine(devices, null, new WebViewOptions { ProfileFolder = folder });
                    try
                    {
                        listed = second.Downloads.Count;
                        forgotten = second.ForgetDownload(second.Downloads.FirstOrDefault());
                    }
                    finally
                    {
                        second.Dispose();
                    }
                });
                Assert.That(first.Writes, Is.True);
            }

            Assert.Multiple(() =>
            {
                Assert.That(listed, Is.EqualTo(1));
                Assert.That(forgotten, Is.True, "off its own list");
                Assert.That(File.ReadAllText(file), Is.EqualTo(list), "the file is the first run's");
            });
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    private static string JsonEncoded(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    private static bool ReadsCompleted(string file, string name)
    {
        try
        {
            using var list = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            return list.RootElement.EnumerateArray().Any(download =>
                download.GetProperty("Name").GetString() == name &&
                download.GetProperty("State").GetString() == nameof(WebDownloadState.Completed));
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException
                                              or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    [Test]
    public void ADownloadStartedByAddress_IsAskedOfTheView_AndSavedInAFolderUnderAFreeName()
    {
        using var site = new LocalSite("<title>files</title>", fileBytes: 30_000);
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumDownloads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "same.bin"), "there before");
        var asked = new List<WebDownload>();
        var started = false;
        string pageAfter = null;
        try
        {
            loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
            {
                started = browser.StartDownload(site.Origin + "/download/same.bin");
                Pump(() => view.Downloads.Count > 0, TimeSpan.FromSeconds(10));
                // Saved before the second is asked: its file is not there yet, but its name is taken.
                foreach (var download in view.Downloads)
                {
                    new WebDownloadRequestedEventArgs(download).SaveInFolder(folder);
                    asked.Add(download);
                }

                browser.StartDownload(site.Origin + "/download/same.bin");
                Pump(() => view.Downloads.Count > 1, TimeSpan.FromSeconds(10));
                foreach (var download in view.Downloads.Skip(1))
                {
                    new WebDownloadRequestedEventArgs(download).SaveInFolder(folder);
                    asked.Add(download);
                }

                Pump(() => asked.All(download => download.IsFinished), TimeSpan.FromSeconds(10));
                pageAfter = browser.Address;
            }));

            Assert.Multiple(() =>
            {
                Assert.That(started, Is.True);
                Assert.That(asked.Select(download => Path.GetFileName(download.FilePath)),
                    Is.EqualTo(new[] { "same (1).bin", "same (2).bin" }));
                Assert.That(asked.Select(download => download.State),
                    Is.All.EqualTo(WebDownloadState.Completed));
                Assert.That(File.ReadAllText(Path.Combine(folder, "same.bin")), Is.EqualTo("there before"));
                Assert.That(new FileInfo(Path.Combine(folder, "same (2).bin")).Length, Is.EqualTo(30_000));
                Assert.That(pageAfter, Is.EqualTo(site.Origin + "/"), "the page stays");
                Assert.That(Throws(() => new WebDownloadRequestedEventArgs(asked[0]).SaveInFolder("relative")), Is.True);
            });
        }
        finally
        {
            loop.Run(() =>
            {
                foreach (var download in asked)
                {
                    engine.ForgetDownload(download);
                }
            });
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
