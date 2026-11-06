using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="ColorPaletteSwatch"/>: a button, called by its color, that says whether it holds
/// the color its palette has selected - so a screen reader and a scenario can tell which one is - and is selected by
/// being pressed.</summary>
public class ColorPaletteSwatchAutomationPeer : ButtonAutomationPeer, ISelectionItemProvider
{
    public ColorPaletteSwatchAutomationPeer(ColorPaletteSwatch owner) : base(owner)
    {
    }

    public bool IsSelected => ((ColorPaletteSwatch)Owner).IsSelected;

    public AutomationPeer SelectionContainer
    {
        get
        {
            for (var node = (IUIComponent)Owner; node != null; node = FocusManager.ParentOf(node))
            {
                if (node is ColorPalette palette)
                {
                    return palette.GetAutomationPeer();
                }
            }

            return null;
        }
    }

    public void Select() => Invoke();
}
