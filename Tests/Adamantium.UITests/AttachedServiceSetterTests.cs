using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// A style setter names an attached property by its owner - <c>Grid.Row</c>, and just as well a static service such as
/// <c>AutomationProperties.AutomationId</c> or <c>KeyTipService.KeyTip</c>. Only component owners were looked for, so a
/// service's property was passed over without a word: an item container style could not give its items their ids.
/// </summary>
[TestFixture]
public class AttachedServiceSetterTests
{
    [Test]
    public void ASetter_ForAServicesAttachedProperty_Applies()
    {
        var button = new Button();

        new Setter("AutomationProperties.AutomationId", "Save").Apply(button, new Style(), null);
        new Setter("KeyTipService.KeyTip", "S").Apply(button, new Style(), null);

        Assert.Multiple(() =>
        {
            Assert.That(AutomationProperties.GetAutomationId(button), Is.EqualTo("Save"));
            Assert.That(KeyTipService.GetKeyTip(button), Is.EqualTo("S"));
        });
    }

    [Test]
    public void ASetter_BoundToTheItem_GivesTheContainerItsOwnId()
    {
        var tab = new TabItem { DataContext = new Page { Header = "Docking" } };

        new Setter("AutomationProperties.AutomationId", new Binding(nameof(Page.Header))).Apply(tab, new Style(), null);
        BindingUpdateQueue.Flush();

        Assert.That(AutomationProperties.GetAutomationId(tab), Is.EqualTo("Docking"));
    }

    private sealed class Page
    {
        public string Header { get; init; }
    }
}
