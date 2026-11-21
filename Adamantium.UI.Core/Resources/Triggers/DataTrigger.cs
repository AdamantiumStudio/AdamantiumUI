using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Core.Resources.Triggers;

public class DataTrigger : TriggerBase
{
    /// <summary>What the trigger tests: any binding - on the host's data, a property of an ancestor, of the host itself.
    /// </summary>
    public BindingBase Binding { get; set; }
    
    public object Value { get; set; }

    public override ITriggerActivator Apply(ITriggerExecutionContext context)
    {
        var activator = new DataTriggerActivator(context, this);
        activator.Activate();
        return activator;
    }
}