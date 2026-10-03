using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary><c>{Localize Table.Key}</c> is a binding to a language table: built by the generator in an application and
/// by the loader in the designer, and the two must agree.</summary>
[TestFixture]
public class LocalizeMarkupTests
{
    private const string English = """
        <Language>
            <Phrase Key="Close">Close</Phrase>
            <Phrase Key="PageOf">Page {page} of {count}</Phrase>
            <Phrase Key="Files" Count="count" One="{count} file" Other="{count} files"/>
            <Phrase Key="Notify" Select="on" True="Notifications on" False="Notifications off"/>
        </Language>
        """;

    private const string Russian = """
        <Language>
            <Phrase Key="Close">Закрыть</Phrase>
            <Phrase Key="PageOf">{count}: страница {page}</Phrase>
            <Phrase Key="Files" Count="count" One="{count} файл" Few="{count} файла" Many="{count} файлов" Other="{count} файла"/>
            <Phrase Key="Notify" Select="on" True="Уведомления включены" False="Уведомления выключены"/>
        </Language>
        """;

    // Other tests load tables named Test.App.Strings into this process too; a namespace of its own keeps the designer's
    // lookup unambiguous.
    private static readonly Lazy<GeneratedProject> DesignerTables = new(() => AumlCodegenHarness.Project(
        new Dictionary<string, string>
        {
            ["Strings.en.alang"] = English,
            ["Strings.ru.alang"] = Russian,
        },
        new Dictionary<string, string> { ["RootNamespace"] = "Test.Designer" }));

    private int _budget;

    [SetUp]
    public void EveryUpdateInOneFlush()
    {
        _budget = BindingUpdateQueue.MaxAppliesPerFlush;
        BindingUpdateQueue.MaxAppliesPerFlush = 0;
    }

    [TearDown]
    public void BackToBase()
    {
        Languages.Current = null;
        BindingUpdateQueue.MaxAppliesPerFlush = _budget;
    }

    [Test]
    public void AView_BindsToItsProjectsTable()
    {
        var project = Project("""
            <StackPanel>
                <TextBlock Text="{Localize Strings.Close}"/>
                <TextBlock Text="{Localize Strings.PageOf, page={Binding Page}, count=3}"/>
            </StackPanel>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(project.Errors, Is.Empty, AumlCodegenHarness.Errors(project.Errors));
            Assert.That(project.Source, Does.Contain("new global::Adamantium.UI.Core.Localization.Localize(global::Test.App.Strings.Current, nameof(global::Test.App.Strings.Close))"));
            Assert.That(project.Source, Does.Contain(".Arguments[\"count\"] = \"3\";"));
        });
    }

    // Where a theme writes its strings: a setter's value and an element of a control template.
    [Test]
    public void AStyleSet_TakesItInASetterAndInATemplate()
    {
        var project = AumlCodegenHarness.Project(new Dictionary<string, string>
        {
            ["Strings.en.alang"] = English,
            ["Strings.ru.alang"] = Russian,
            ["PagerStyleSet.auml"] = """
                <StyleSet xmlns="http://adamantium/ui" xmlns:x="http://adamantium/ui/xaml/extensions">
                    <Style Selector="Button">
                        <Setter Property="ToolTip" Value="{Localize Strings.Close}"/>
                    </Style>
                    <Style Selector="ContentControl">
                        <Setter Property="Template">
                            <Setter.Value>
                                <ControlTemplate TargetType="ContentControl">
                                    <StackPanel>
                                        <TextBlock Text="{Localize Strings.Close}"/>
                                        <TextBlock Text="{Localize Strings.PageOf, page=1, count={Binding Tag}}"/>
                                        <TextBlock Text="{Localize Strings.PageOf, page={TemplateBinding Content}, count=3}"/>
                                    </StackPanel>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </StyleSet>
                """,
        });

        Assert.Multiple(() =>
        {
            Assert.That(project.Errors, Is.Empty, AumlCodegenHarness.Errors(project.Errors));
            Assert.That(project.Source, Does.Contain(".Arguments[\"page\"] = tb"), "the control's own value, followed");
        });
    }

    // Said by the generator at the markup, before the table is even compiled.
    [TestCase("{Localize Strings.Missing}", "Strings has no string 'Missing'", TestName = "A key the table lacks")]
    [TestCase("{Localize Strings.PageOf, pages={Binding Page}, count=3}", "Strings.PageOf has no placeholder 'pages': it fills page, count",
        TestName = "An argument the string lacks")]
    [TestCase("{Localize Strings.PageOf, page=1}", "Strings.PageOf needs count", TestName = "A placeholder left unfilled")]
    [TestCase("{Localize Strings.Close, page=1}", "Strings.Close has no placeholder 'page': it has no placeholders",
        TestName = "An argument to a string without placeholders")]
    public void AWrongKeyOrArgument_FailsTheBuild(string localize, string message)
    {
        var project = Project($"""<TextBlock Text="{localize}"/>""");

        Assert.That(project.Errors.Select(e => e.GetMessage()), Has.Some.Contains(message));
    }

    [Test]
    public void TheString_FollowsItsArgumentsAndTheLanguage()
    {
        var table = DesignerTable();
        var page = new Reading { Number = 2 };
        var text = new TextBlock();
        var localize = new Localize(table, "PageOf");
        localize.Arguments["page"] = new Binding(nameof(Reading.Number)) { Source = page };
        localize.Arguments["count"] = 5;
        text.SetBinding(TextBlock.TextProperty, localize);

        var first = text.Text;
        page.Number = 3;
        BindingUpdateQueue.Flush();
        var moved = text.Text;
        Languages.Current = "ru";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("Page 2 of 5"));
            Assert.That(moved, Is.EqualTo("Page 3 of 5"));
            Assert.That(text.Text, Is.EqualTo("5: страница 3"));
        });
    }

    [Test]
    public void ACountedString_TakesTheFormOfItsNumber_AsTheNumberChanges()
    {
        var files = new Reading { Number = 1 };
        var text = new TextBlock();
        var localize = new Localize(DesignerTable(), "Files");
        localize.Arguments["count"] = new Binding(nameof(Reading.Number)) { Source = files };
        text.SetBinding(TextBlock.TextProperty, localize);

        var one = text.Text;
        files.Number = 2;
        BindingUpdateQueue.Flush();
        var two = text.Text;
        Languages.Current = "ru";
        files.Number = 5;
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(one, Is.EqualTo("1 file"));
            Assert.That(two, Is.EqualTo("2 files"));
            Assert.That(text.Text, Is.EqualTo("5 файлов"));
        });
    }

    // Bound once: the words follow the state and the language both.
    [Test]
    public void AStringChosenByAValue_FollowsTheValueAndTheLanguage()
    {
        var state = new Switch { On = true };
        var text = new TextBlock();
        var localize = new Localize(DesignerTable(), "Notify");
        localize.Arguments["on"] = new Binding(nameof(Switch.On)) { Source = state };
        text.SetBinding(TextBlock.TextProperty, localize);

        var on = text.Text;
        state.On = false;
        BindingUpdateQueue.Flush();
        var off = text.Text;
        Languages.Current = "ru";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(on, Is.EqualTo("Notifications on"));
            Assert.That(off, Is.EqualTo("Notifications off"));
            Assert.That(text.Text, Is.EqualTo("Уведомления выключены"));
        });
    }

    private sealed class Switch : INotifyPropertyChanged
    {
        private bool _on;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool On
        {
            get => _on;
            set
            {
                _on = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(On)));
            }
        }
    }

    // A control's template fills a string with the control's own values. The part learns which control that is only
    // once the template is stamped onto it, which is after its bindings were made.
    [Test]
    public void InATemplate_TheStringTakesTheControlsOwnValue()
    {
        var table = DesignerTable();
        TextBlock text = null;
        var control = new ContentControl
        {
            Content = 2,
            Template = new ControlTemplate(() =>
            {
                text = new TextBlock();
                var localize = new Localize(table, "PageOf");
                localize.Arguments["page"] = new TemplateBinding { Path = nameof(ContentControl.Content) };
                localize.Arguments["count"] = 5;
                text.SetBinding(TextBlock.TextProperty, localize);
                return new TemplateResult { RootComponent = text };
            })
        };
        var root = new Root();
        root.Children.Add(control);
        root.Measure(new Size(300, 100), force: true);
        root.Arrange(new Rect(0, 0, 300, 100));
        BindingUpdateQueue.Flush();

        var first = text?.Text;
        control.Content = 3;
        BindingUpdateQueue.Flush();
        var moved = text?.Text;
        Languages.Current = "ru";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("Page 2 of 5"));
            Assert.That(moved, Is.EqualTo("Page 3 of 5"));
            Assert.That(text?.Text, Is.EqualTo("5: страница 3"));
        });
    }

    [Test]
    public void AMultiBinding_TakesItAsOneOfItsBindings()
    {
        var text = new TextBlock();
        var multi = new MultiBinding { StringFormat = "[{0}]" };
        multi.Bindings.Add(new Localize(DesignerTable(), "Close"));
        text.SetBinding(TextBlock.TextProperty, multi);

        var english = text.Text;
        Languages.Current = "ru";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(english, Is.EqualTo("[Close]"));
            Assert.That(text.Text, Is.EqualTo("[Закрыть]"));
        });
    }

    // The word for a kind of thing, its key read from a binding; and the table itself, handed to a control that names
    // things in code.
    [Test]
    public void AKeyReadFromABinding_AndTheTableItself()
    {
        var project = Project("""
            <StackPanel>
                <TextBlock Text="{Localize Strings, Key={Binding Kind}}"/>
                <ContentControl Tag="{x:Static Strings.Current}"/>
            </StackPanel>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(project.Errors, Is.Empty, AumlCodegenHarness.Errors(project.Errors));
            Assert.That(project.Source, Does.Contain(".KeySource = binding"));
            Assert.That(project.Source, Does.Contain("global::Test.App.Strings.Current"));
        });
    }

    // A phrase inside a phrase: "Page 2 files of 3" where the inner one takes the form of its own number.
    [Test]
    public void APhraseAsAnArgument_IsAPhraseNotItsText()
    {
        var project = Project("""
            <TextBlock Text="{Localize Strings.PageOf, page={Localize Strings.Files, count={Binding Count}}, count=3}"/>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(project.Errors, Is.Empty, AumlCodegenHarness.Errors(project.Errors));
            Assert.That(project.Source, Does.Match(@"\.Arguments\[""page""\] = localized_\d+;"));
            Assert.That(project.Source, Does.Not.Contain("\"{Localize"));
        });
    }

    [Test]
    public void TheDesigner_SaysAPhraseInsideAPhrase_AndFollowsTheLanguage()
    {
        var tables = DesignerTables.Value;
        Assert.That(tables.Errors, Is.Empty, AumlCodegenHarness.Errors(tables.Errors));
        tables.Load();

        var load = AumlLoader.Load($$"""
            <TextBlock xmlns="http://adamantium/ui" xmlns:t="clr-namespace:Test.Designer;assembly={{tables.Compilation.AssemblyName}}"
                       Text="{Localize t:Strings.PageOf, page={Localize t:Strings.Files, count=2}, count=5}"/>
            """);
        Assert.That(load.Root, Is.InstanceOf<TextBlock>(), string.Join(" | ", load.Diagnostics));
        var text = (TextBlock)load.Root;
        var english = text.Text;

        Languages.Current = "ru";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(load.Diagnostics, Is.Empty);
            Assert.That(english, Is.EqualTo("Page 2 files of 5"));
            Assert.That(text.Text, Is.EqualTo("5: страница 2 файла"));
        });
    }

    [Test]
    public void AKeyWrittenWhereABindingBelongs_FailsTheBuild()
    {
        var project = Project("""<TextBlock Text="{Localize Strings, Key=Close}"/>""");

        Assert.That(project.Errors.Select(e => e.GetMessage()), Has.Some.Contains("reads the key from a binding"));
    }

    [Test]
    public void AKeyReadFromABinding_IsTheWordOrTheKeyItself()
    {
        var kind = new Reading { Number = 0 };
        var text = new TextBlock();
        var localize = new Localize(DesignerTable(), null) { KeySource = new Binding(nameof(Reading.Word)) { Source = kind } };
        text.SetBinding(TextBlock.TextProperty, localize);

        kind.Word = "Close";
        BindingUpdateQueue.Flush();
        var known = text.Text;
        kind.Word = "Brush";
        BindingUpdateQueue.Flush();
        var unknown = text.Text;
        kind.Word = "Close";
        Languages.Current = "ru";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(known, Is.EqualTo("Close"));
            Assert.That(unknown, Is.EqualTo("Brush"), "a word the table lacks is said as it is");
            Assert.That(text.Text, Is.EqualTo("Закрыть"));
        });
    }

    [Test]
    public void ATableNobodyHas_FailsTheBuild()
    {
        var project = Project("""<TextBlock Text="{Localize Missing.Close}"/>""");

        Assert.That(project.Errors.Select(e => e.GetMessage()), Has.Some.Contains("No language table 'Missing'"));
    }

    [TestCase("Strings", TestName = "A table of the project")]
    [TestCase("t:Strings", TestName = "A table named through its namespace")]
    public void TheDesigner_ShowsTheString_AndFollowsTheLanguage(string table)
    {
        var tables = DesignerTables.Value;
        Assert.That(tables.Errors, Is.Empty, AumlCodegenHarness.Errors(tables.Errors));
        tables.Load();

        var load = AumlLoader.Load($$"""
            <StackPanel xmlns="http://adamantium/ui" xmlns:t="clr-namespace:Test.Designer;assembly={{tables.Compilation.AssemblyName}}">
                <TextBlock Text="{Localize {{table}}.Close}"/>
                <TextBlock Text="{Localize {{table}}.PageOf, page=2, count=5}"/>
            </StackPanel>
            """);
        Assert.That(load.Root, Is.InstanceOf<StackPanel>(), string.Join(" | ", load.Diagnostics));
        var close = (TextBlock)((StackPanel)load.Root).Children[0];
        var page = (TextBlock)((StackPanel)load.Root).Children[1];

        Assert.Multiple(() =>
        {
            Assert.That(load.Diagnostics, Is.Empty);
            Assert.That(close.Text, Is.EqualTo("Close"));
            Assert.That(page.Text, Is.EqualTo("Page 2 of 5"));
        });

        Languages.Current = "ru";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(close.Text, Is.EqualTo("Закрыть"));
            Assert.That(page.Text, Is.EqualTo("5: страница 2"));
        });
    }

    private static LocalizedStrings DesignerTable()
    {
        var tables = DesignerTables.Value;
        Assert.That(tables.Errors, Is.Empty, AumlCodegenHarness.Errors(tables.Errors));
        return Localize.TableOf(tables.Load().GetType("Test.Designer.Strings"));
    }

    private sealed class Root : Grid, IRootVisualComponent
    {
        public Vector2 PointToClient(PixelPoint point) => new((float)point.X, (float)point.Y);
        public PixelPoint PointToScreen(Vector2 point) => new(point.X, point.Y);
        public PixelPoint Position { get; set; }
        public void AttachContextAndInitialize(IUIContext context) { }
        public double Left { get; set; }
        public double Top { get; set; }
        public string Title { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public IUIContext UIContext => null;
    }

    private sealed class Reading : INotifyPropertyChanged
    {
        private int _number;
        private string _word;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Word
        {
            get => _word;
            set
            {
                _word = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Word)));
            }
        }

        public int Number
        {
            get => _number;
            set
            {
                _number = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Number)));
            }
        }
    }

    private static GeneratedProject Project(string content) => AumlCodegenHarness.Project(new Dictionary<string, string>
    {
        ["Strings.en.alang"] = English,
        ["Strings.ru.alang"] = Russian,
        ["MainWindow.auml"] = AumlCodegenHarness.WindowHeader + ">" + content + "</Window>",
    });
}
