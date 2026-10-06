using System.Collections.Generic;
using NUnit.Framework;

namespace Adamantium.XamlTests;

[TestFixture]
public class AumlLocalTypePropertyTests
{
    private const string Controls = """
        using Adamantium.UI.Controls.Base;
        using Adamantium.UI.Core;
        using Adamantium.UI.Core.Media;

        namespace Test.App.Controls;

        public sealed class Gutter : InputUIComponent
        {
            public static readonly AdamantiumProperty MarkBrushProperty = AdamantiumProperty.Register(nameof(MarkBrush),
                typeof(Brush), typeof(Gutter), new PropertyMetadata(null));

            public Brush MarkBrush
            {
                get => GetValue<Brush>(MarkBrushProperty);
                set => SetValue(MarkBrushProperty, value);
            }
        }

        public class Editor : Control
        {
        }
        """;

    private const string StyleSet = """
        <StyleSet xmlns="http://adamantium/ui"
                  xmlns:x="http://adamantium/ui/xaml/extensions"
                  xmlns:local="clr-namespace:Test.App.Controls">
            <Style Selector="Editor">
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="local:Editor">
                            <local:Gutter Foreground="Red"
                                          MarkBrush="Blue"/>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </StyleSet>
        """;

    [Test]
    public void AnElementOfTheProjectItself_TakesItsOwnAndInheritedProperties_InATemplate()
    {
        var project = AumlCodegenHarness.Project(
            new Dictionary<string, string> { ["Themes/EditorStyleSet.auml"] = StyleSet }, sources: [Controls]);

        Assert.That(project.Errors, Is.Empty, AumlCodegenHarness.Errors(project.Errors));
    }
}
