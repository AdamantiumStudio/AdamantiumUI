using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A list's groups are written in markup as a WPF list's are: a <c>GroupStyle</c> in
/// <c>ItemsControl.GroupStyle</c>, its header a <c>DataTemplate</c>.</summary>
[TestFixture]
public class GroupStyleMarkupTests
{
    [Test]
    public void AGroupStyleWithAHeaderTemplate_Compiles()
    {
        var auml = AumlCodegenHarness.WindowHeader + """
            >
                <ItemsControl>
                    <ItemsControl.GroupStyle>
                        <GroupStyle CanCollapse="True">
                            <GroupStyle.HeaderTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Name}"/>
                                </DataTemplate>
                            </GroupStyle.HeaderTemplate>
                        </GroupStyle>
                    </ItemsControl.GroupStyle>
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel ItemWidth="56" ItemHeight="56"/>
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                </ItemsControl>
            </Window>
            """;

        AumlCodegenHarness.Generate(auml, out var errors);
        IReadOnlyList<Diagnostic> compiled = AumlCodegenHarness.Compile(auml);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
            Assert.That(compiled, Is.Empty, AumlCodegenHarness.Errors(compiled));
        });
    }
}
