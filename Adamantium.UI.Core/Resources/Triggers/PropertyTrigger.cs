namespace Adamantium.UI.Core.Resources.Triggers;

public class PropertyTrigger : TriggerBase
{
    public string Property { get; set; }

    /// <summary>A named template part whose property is watched instead of the host's; the counterpart of
    /// <see cref="ISetter.TargetName"/>.</summary>
    public string SourceName { get; set; }

    public Object Value { get; set; }

    public override ITriggerActivator Apply(ITriggerExecutionContext context)
    {
        var activator = new PropertyTriggerActivator(context,this);
        activator.Activate();
        return activator;
    }
}
