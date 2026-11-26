using System.Threading.Tasks;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A window closed in a headless application is discarded as in a running one. The discard asks the
/// application for its region manager, and without one it threw, so a closed window stayed in its brushes' trees for
/// the rest of the test run.</summary>
[TestFixture]
public class HeadlessApplicationTests
{
    [Test]
    public void AClosedWindow_IsDiscardedWithItsContent()
    {
        HeadlessApplication.Start<Fluent>();
        var content = new Border();
        var window = new Window { Width = 200, Height = 100, Content = content };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);

        window.Close();
        LoopSignal.Drain();
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.Multiple(() =>
        {
            Assert.That(window.IsDiscarded, Is.True, "the window");
            Assert.That(content.IsDiscarded, Is.True, "its content");
        });
    }

    // A window made before a language came in and listed after it reads the language once listed, as a theme swap
    // reaches a window that missed it.
    [Test]
    public async Task AWindowListedAfterALanguageChange_ReadsItOnceListed()
    {
        var language = Adamantium.Localization.Languages.Current;
        try
        {
            Adamantium.Localization.Languages.Current = "en-US";
            var application = HeadlessApplication.Start<Fluent>();
            var calendar = new Calendar { DisplayDate = new System.DateTime(2026, 10, 1) };
            var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = calendar };
            window.AttachContextAndInitialize(application);
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            var before = calendar.HeaderText;

            Adamantium.Localization.Languages.Current = "ru-RU";
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            var unlisted = calendar.HeaderText;
            await using var session = AutomationSession.InProcess(window);
            await session.WaitForIdleAsync();

            Assert.Multiple(() =>
            {
                Assert.That(before, Is.EqualTo("October 2026"));
                Assert.That(unlisted, Is.EqualTo("October 2026"), "an unlisted window is told nothing");
                Assert.That(calendar.HeaderText, Does.StartWith("октябрь"), "once listed, the window reads the language");
            });
        }
        finally
        {
            Adamantium.Localization.Languages.Current = language;
        }
    }
}
