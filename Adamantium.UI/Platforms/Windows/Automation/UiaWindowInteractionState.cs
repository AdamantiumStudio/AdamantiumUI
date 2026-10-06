namespace Adamantium.UI.Platforms.Windows.Automation;

internal enum UiaWindowInteractionState
{
    Running = 0,
    Closing = 1,
    ReadyForUserInteraction = 2,
    BlockedByModalWindow = 3,
    NotResponding = 4
}
