using System.Collections.Generic;

namespace Adamantium.UI.Core.Resources.Triggers;

public abstract class TriggerBase : ITrigger
{
    protected TriggerBase()
    {

    }

    public SetterCollection Setters { get; set; }

    /// <summary>Actions run when the trigger's condition becomes true (e.g. start an animation). WPF EnterActions analog.</summary>
    public List<ITriggerAction> EnterActions { get; } = new();

    /// <summary>Actions run when the trigger's condition becomes false again. WPF ExitActions analog.</summary>
    public List<ITriggerAction> ExitActions { get; } = new();

    public void Add(ISetter setter)
    {
        Setters ??= [];
        Setters.Add(setter);
    }

    public abstract ITriggerActivator Apply(ITriggerExecutionContext context);
}