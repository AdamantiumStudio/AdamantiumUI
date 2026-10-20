using Adamantium.Graphics.Core;

namespace Adamantium.UI.Core.RoutedEvents;

public class MSAALevelChangedEventArgs : RoutedEventArgs
{
    public MSAALevel MSAALevel { get; }

    public MSAALevelChangedEventArgs(MSAALevel level)
    {
        MSAALevel = level;
    }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is MSAALevelChangedHandler typed)
        {
            typed(target, this);
            return;
        }

        base.InvokeEventHandler(handler, target);
    }
}