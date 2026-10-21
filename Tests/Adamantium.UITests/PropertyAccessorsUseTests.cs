using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

// What the UI binds to is reached without reflection: the components are described by the generator (their base asks
// for it), and a path into a plain object still works, through reflection. A reflected accessor's delegates are bound
// to a PropertyInfo or a FieldInfo; a generated one's are not.
[TestFixture]
public class PropertyAccessorsUseTests
{
    [Test]
    public void TheFrameworksComponents_AreReachedWithoutReflection()
    {
        var border = new Border { Width = 40 };
        var width = PropertyAccessors.Find(typeof(Border), nameof(Border.Width));
        var child = PropertyAccessors.Find(typeof(Border), nameof(Border.Child));
        var text = PropertyAccessors.Find(typeof(TextBox), nameof(TextBox.Text));
        var plain = PropertyAccessors.Find(typeof(Plain), nameof(Plain.Title));

        Assert.Multiple(() =>
        {
            Assert.That(width.GetValue(border), Is.EqualTo(40d));
            Assert.That(child.CanWrite, Is.True);
            Assert.That(text.ValueType, Is.EqualTo(typeof(string)));
            Assert.That(IsGenerated(width), Is.True);
            Assert.That(IsGenerated(child), Is.True);
            Assert.That(IsGenerated(text), Is.True);
            Assert.That(IsGenerated(plain), Is.False, "a plain object is reached through reflection");
        });
    }

    [Test]
    public void ABindingIntoAPlainObject_StillReadsAndWritesIt()
    {
        var source = new Plain { Inner = new Plain { Title = "first" } };
        var box = new TextBox();
        box.SetBinding(TextBox.TextProperty, new Binding("Inner.Title") { Source = source, Mode = BindingMode.TwoWay });

        Assert.That(box.Text, Is.EqualTo("first"));

        box.Text = "second";
        Assert.That(source.Inner.Title, Is.EqualTo("second"));
    }

    private static bool IsGenerated(PropertyAccessor accessor) =>
        accessor.Getter?.Target is not System.Reflection.MemberInfo && accessor.Setter?.Target is not System.Reflection.MemberInfo;

    public sealed class Plain
    {
        public Plain Inner { get; set; }

        public string Title { get; set; }
    }
}
