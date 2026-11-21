using System.Collections.Generic;

namespace Adamantium.UI.Core.Resources.Triggers;

/// <summary>Applies its setters and Enter/Exit actions only while ALL of its <see cref="Conditions"/> hold at once, each a
/// <c>{Binding}</c> on the host's data resolving to its value - the WPF MultiDataTrigger. What a view model's several
/// values decide together, as "this kind AND that switch on", is written here rather than in a multi-value converter.
/// The conditions go in <c>&lt;MultiDataTrigger.Conditions&gt;</c>.</summary>
public class MultiDataTrigger : TriggerBase
{
    /// <summary>The binding=value tests that must ALL be true for the trigger to be active.</summary>
    public List<Condition> Conditions { get; } = [];

    public override ITriggerActivator Apply(ITriggerExecutionContext context)
    {
        var activator = new MultiDataTriggerActivator(context, this);
        activator.Activate();
        return activator;
    }
}
