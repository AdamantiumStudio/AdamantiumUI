using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Markup;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A saved .auml reaches the running application: generated code reports each view it builds, and a save is applied to
/// every live instance in place. A save that does not build leaves the running version, and a view made after an edit
/// takes it at once instead of showing what the build compiled.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MarkupHotReloadTests
{
    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private string _directory;
    private string _file;
    private List<MarkupReloadReport> _failures;
    private Action<MarkupReloadReport> _failed;

    private static string Panel(string caption) =>
        $"<StackPanel {Namespaces}><TextBlock Text=\"{caption}\"/><TextBlock Text=\"Stays\"/></StackPanel>";

    [SetUp]
    public void Fresh()
    {
        var app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(app, null);
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, app);
        MarkupHotReload.Reset();
        MarkupHotReload.Dispatch = action => action();
        MarkupHotReload.Mode = MarkupReloadMode.Manual;
        Design.IsDesignMode = false;
        _directory = Path.Combine(Path.GetTempPath(), "auml-hot-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _file = Path.Combine(_directory, "Page.auml");
        _failures = [];
        _failed = report => _failures.Add(report);
        MarkupHotReload.Failed += _failed;
    }

    [TearDown]
    public void CleanUp()
    {
        MarkupHotReload.Failed -= _failed;
        MarkupHotReload.Reset();
        MarkupHotReload.Dispatch = LoopSignal.Post;
        MarkupHotReload.Mode = MarkupReloadMode.OnSave;
        Directory.Delete(_directory, true);
    }

    [Test]
    public void ADebugBuild_ReportsEachViewItBuilds()
    {
        var view = $"<View x:Namespace=\"Test.App\" {Namespaces}><TextBlock Text=\"Hi\"/></View>";

        var project = AumlCodegenHarness.Project(new Dictionary<string, string> { ["Page.auml"] = view },
            new Dictionary<string, string> { [Adamantium.UI.Generators.AumlCodeBehindGenerator.HotReloadProperty] = "true" });

        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors));
        Assert.That(project.Source,
            Does.Contain($"MarkupHotReload.Track(this, \"C:\\\\Test\\\\Page.auml\", \"{MarkupHash.Of(view)}\");"));
    }

    [Test]
    public void ABuildWithoutIt_ReportsNothing()
    {
        var view = $"<View x:Namespace=\"Test.App\" {Namespaces}><TextBlock Text=\"Hi\"/></View>";

        var project = AumlCodegenHarness.Project(new Dictionary<string, string> { ["Page.auml"] = view });

        Assert.That(project.Source, Does.Not.Contain("MarkupHotReload"));
    }

    [Test]
    public async Task ASavedEdit_LandsOnTheLiveInstance_InPlace()
    {
        var panel = Built(Panel("Before"));
        var edited = (TextBlock)panel.Children[0];
        var kept = panel.Children[1];

        File.WriteAllText(_file, Panel("After"));
        var reports = await MarkupHotReload.ApplyAsync([_file]);

        Assert.That(reports.Single().Applied, Is.True, string.Join(" | ", reports.Single().Messages));
        Assert.Multiple(() =>
        {
            Assert.That(reports.Single().Instances, Is.EqualTo(1));
            Assert.That(panel.Children[0], Is.SameAs(edited));
            Assert.That(panel.Children[1], Is.SameAs(kept));
            Assert.That(edited.Text, Is.EqualTo("After"));
        });
    }

    [Test]
    public async Task ASaveThatDoesNotBuild_LeavesTheRunningVersion()
    {
        var panel = Built(Panel("Before"));

        File.WriteAllText(_file, "<StackPanel " + Namespaces + "><TextBlock Text=\"Broken\"</StackPanel>");
        var reports = await MarkupHotReload.ApplyAsync([_file]);

        Assert.Multiple(() =>
        {
            Assert.That(reports.Single().Applied, Is.False);
            Assert.That(_failures, Has.Count.EqualTo(1));
            Assert.That(((TextBlock)panel.Children[0]).Text, Is.EqualTo("Before"));
        });
    }

    [Test]
    public async Task AViewMadeAfterAnEdit_TakesTheEditAtOnce()
    {
        Built(Panel("Before"));
        File.WriteAllText(_file, Panel("After"));
        await MarkupHotReload.ApplyAsync([_file]);

        var later = (StackPanel)AumlLoader.Load(Panel("Before")).Root;
        MarkupHotReload.Track(later, _file, MarkupHash.Of(Panel("Before")));

        Assert.That(((TextBlock)later.Children[0]).Text, Is.EqualTo("After"));
    }

    [Test]
    public async Task AViewBuiltByCodeTheIdeAlreadyUpdated_IsNotEditedAgain()
    {
        Built(Panel("Before"));
        var edited = $"<StackPanel {Namespaces}><TextBlock Text=\"After\"/><TextBlock Text=\"Stays\"/><Button Content=\"New\"/></StackPanel>";
        File.WriteAllText(_file, edited);
        await MarkupHotReload.ApplyAsync([_file]);

        var later = (StackPanel)AumlLoader.Load(edited).Root;
        MarkupHotReload.Track(later, _file, MarkupHash.Of(edited));

        Assert.That(later.Children.Count, Is.EqualTo(3), "the added child once, not twice");
    }

    [Test]
    public async Task AFileChangedSinceTheBuild_IsNotEditedBlindly()
    {
        File.WriteAllText(_file, Panel("Edited before the run"));
        var panel = (StackPanel)AumlLoader.Load(Panel("Before")).Root;
        MarkupHotReload.Track(panel, _file, MarkupHash.Of(Panel("Before")));

        File.WriteAllText(_file, Panel("After"));
        var reports = await MarkupHotReload.ApplyAsync([_file]);

        Assert.That(reports.Single().Applied, Is.False);
        Assert.That(((TextBlock)panel.Children[0]).Text, Is.EqualTo("Before"));
    }

    [Test]
    public void InManualMode_ASaveIsGathered_NotApplied()
    {
        var panel = Built(Panel("Before"));

        File.WriteAllText(_file, Panel("After"));
        MarkupHotReload.NotifyChanged(_file);

        Assert.That(MarkupHotReload.ChangedFiles, Is.EqualTo((string[])[_file]));
        Assert.That(((TextBlock)panel.Children[0]).Text, Is.EqualTo("Before"));
    }

    [Test]
    public void AStyleSetOfADebugBuild_ReportsItself()
    {
        var styles = $"<StyleSet {Namespaces}><Style Selector=\"Border\"><Setter Property=\"Width\" Value=\"10\"/></Style></StyleSet>";

        var project = AumlCodegenHarness.Project(new Dictionary<string, string> { ["Styles.auml"] = styles },
            new Dictionary<string, string> { [Adamantium.UI.Generators.AumlCodeBehindGenerator.HotReloadProperty] = "true" });

        Assert.That(project.Errors, Is.Empty, string.Join(" | ", project.Errors));
        Assert.That(project.Source, Does.Contain($"MarkupHotReload.Track(this, \"C:\\\\Test\\\\Styles.auml\", \"{MarkupHash.Of(styles)}\");"));
    }

    [Test]
    public async Task AnEditedStyleSet_IsBuiltAgain_AndItsThemeTakesIt()
    {
        static string Styles(string width, string more) =>
            $"<StyleSet {Namespaces}><Style Selector=\"Border\"><Setter Property=\"Width\" Value=\"{width}\"/></Style>{more}</StyleSet>";
        var set = Built<StyleSet>(Styles("10", string.Empty));
        var theme = new Theme("Probe");
        theme.AddStyleSet(set);
        var before = set.Styles[0];

        File.WriteAllText(_file, Styles("20", "<Style Selector=\"TextBlock\"/>"));
        var reports = await MarkupHotReload.ApplyAsync([_file]);
        theme.RefreshFromMarkup();

        Assert.That(reports.Single().Applied, Is.True, string.Join(" | ", reports.Single().Messages));
        Assert.Multiple(() =>
        {
            Assert.That(set.Styles.Count, Is.EqualTo(2));
            Assert.That(set.Styles[0].Setters[0].Value, Is.EqualTo("20"));
            Assert.That(theme.MergedStyles.Styles, Does.Not.Contain(before), "the old styles leave the theme");
            Assert.That(theme.MergedStyles.Styles, Is.EquivalentTo(set.Styles));
        });
    }

    [Test]
    public async Task AnEditedVariant_RepaintsThePaletteBrushItself()
    {
        static string Variant(string color) =>
            $"<ThemeVariantDefinition {Namespaces} Key=\"Light\"><ThemeVariantDefinition.Colors>" +
            $"<PaletteColor Key=\"Probe\" Color=\"{color}\"/></ThemeVariantDefinition.Colors></ThemeVariantDefinition>";
        var variant = Built<ThemeVariantDefinition>(Variant("#FF0000"));
        var theme = new Theme("Probe");
        theme.Variants.Add(variant);
        theme.ApplyVariant(variant.Key);
        var brush = theme.Palette["Probe"];

        File.WriteAllText(_file, Variant("#00FF00"));
        var reports = await MarkupHotReload.ApplyAsync([_file]);
        theme.RefreshFromMarkup();

        Assert.That(reports.Single().Applied, Is.True, string.Join(" | ", reports.Single().Messages));
        Assert.Multiple(() =>
        {
            Assert.That(theme.Palette["Probe"], Is.SameAs(brush));
            Assert.That(brush.Color, Is.EqualTo(Color.FromRgba(0, 255, 0, 255)));
        });
    }

    [TestCase(MarkupReloadMode.Manual)]
    [TestCase(MarkupReloadMode.OnSave)]
    public async Task TheIdesApplyButton_AppliesASaveTheWatcherHasNotReportedYet(MarkupReloadMode mode)
    {
        var panel = Built(Panel("Before"));
        MarkupHotReload.Mode = mode;

        File.WriteAllText(_file, Panel("After"));
        await MarkupHotReload.CodeUpdatedAsync();

        Assert.That(((TextBlock)panel.Children[0]).Text, Is.EqualTo("After"));
    }

    // Two writes within one tick of the file clock share a write time, and a save told apart by it was lost - on CI.
    [Test]
    public async Task ASaveWithTheWriteTimeOfTheOneBefore_IsStillApplied()
    {
        var panel = Built(Panel("Before"));
        MarkupHotReload.Mode = MarkupReloadMode.Manual;
        var before = File.GetLastWriteTimeUtc(_file);

        File.WriteAllText(_file, Panel("After"));
        File.SetLastWriteTimeUtc(_file, before);
        MarkupHotReload.NotifyChanged(_file);
        var gathered = MarkupHotReload.ChangedFiles;
        await MarkupHotReload.CodeUpdatedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(gathered, Has.Count.EqualTo(1), "the watcher's report is kept");
            Assert.That(((TextBlock)panel.Children[0]).Text, Is.EqualTo("After"), "the IDE's apply applies it");
        });
    }

    [Test]
    public void ASaveOfWhatIsAlreadyApplied_IsNotGatheredAgain()
    {
        Built(Panel("Before"));
        MarkupHotReload.Mode = MarkupReloadMode.Manual;

        File.WriteAllText(_file, Panel("Before"));
        MarkupHotReload.NotifyChanged(_file);

        Assert.That(MarkupHotReload.ChangedFiles, Is.Empty);
    }

    [Test]
    public void TheRuntimesHotReload_CallsTheHandler()
    {
        var handlers = typeof(MarkupHotReload).Assembly.GetCustomAttributes<MetadataUpdateHandlerAttribute>();

        Assert.That(handlers.Select(handler => handler.HandlerType), Does.Contain(typeof(MarkupHotReloadUpdateHandler)));
        Assert.That(typeof(MarkupHotReloadUpdateHandler).GetMethod("UpdateApplication", [typeof(Type[])]), Is.Not.Null);
    }

    [Test]
    public void TheDesigner_TracksNothing()
    {
        Design.IsDesignMode = true;
        try
        {
            File.WriteAllText(_file, Panel("Before"));
            MarkupHotReload.Track(AumlLoader.Load(Panel("Before")).Root, _file, MarkupHash.Of(Panel("Before")));
            MarkupHotReload.NotifyChanged(_file);

            Assert.That(MarkupHotReload.ChangedFiles, Is.Empty);
        }
        finally
        {
            Design.IsDesignMode = false;
        }
    }

    private StackPanel Built(string markup)
    {
        return Built<StackPanel>(markup);
    }

    private T Built<T>(string markup)
    {
        File.WriteAllText(_file, markup);
        var built = (T)AumlLoader.Load(markup).Root;
        MarkupHotReload.Track(built, _file, MarkupHash.Of(markup));
        return built;
    }
}
