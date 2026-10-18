using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls;

// The tip a ribbon command shows when its author gave it none: its label and its key, as Word's "Bold (Ctrl+B)" - read
// as the tip opens, so a label in another language or a command swapped since shows as it is now.
internal sealed class RibbonCommandToolTip(ButtonBase command)
{
    public override string ToString()
    {
        var label = command.Content as string;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = AutomationProperties.GetName(command);
        }

        var key = command.Command is RoutedCommand { InputGestures: [var gesture, ..] } ? gesture.ToString() : null;
        return string.IsNullOrWhiteSpace(label) ? key ?? string.Empty
            : key == null ? label
            : $"{label} ({key})";
    }
}
