using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;
using NUnit.Framework;

namespace Adamantium.UITests;

// Clearing a property's last value hands callbacks the Default slot's value, never the raw UnsetValue.
[TestFixture]
public class ClearValueTests
{
    private sealed class Probe : AdamantiumComponent
    {
        public static readonly AdamantiumProperty FlagProperty = AdamantiumProperty.Register(
            nameof(Flag), typeof(bool), typeof(Probe), new PropertyMetadata(false, OnFlagChanged));

        public static object LastNewValue;
        public static string Failure;

        private static void OnFlagChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
        {
            LastNewValue = e.NewValue;
            try
            {
                var typed = (bool)e.NewValue;   // exactly what a real callback does
                Failure = null;
                _ = typed;
            }
            catch (System.Exception ex)
            {
                Failure = ex.GetType().Name;
            }
        }

        public bool Flag
        {
            get => GetValue<bool>(FlagProperty);
            set => SetValue(FlagProperty, value);
        }
    }

    [Test]
    public void ClearingTheLastValue_ReportsTheDefault_NotUnset()
    {
        var probe = new Probe { Flag = true };
        Probe.LastNewValue = null;
        Probe.Failure = null;

        probe.ClearValue(Probe.FlagProperty);

        Assert.Multiple(() =>
        {
            Assert.That(Probe.Failure, Is.Null, $"cast of e.NewValue threw; NewValue was {Probe.LastNewValue} ({Probe.LastNewValue?.GetType().Name})");
            Assert.That(Probe.LastNewValue, Is.EqualTo(false), "the callback should see the value the property now reads as");
            Assert.That(probe.Flag, Is.False, "GetValue falls back to the metadata default");
        });
    }
}
