using System.Collections.Generic;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="Hyperlink"/> inside a <see cref="TextBlock"/>: a link, called by its text, invoked
/// as a click on it.</summary>
public class HyperlinkAutomationPeer : AutomationPeer, IInvokeProvider
{
    private readonly TextBlockAutomationPeer _blockPeer;
    private readonly TextBlock _block;

    internal HyperlinkAutomationPeer(TextBlockAutomationPeer blockPeer, TextBlock block, Hyperlink link)
    {
        _blockPeer = blockPeer;
        _block = block;
        Link = link;
    }

    /// <summary>The link this peer stands for.</summary>
    public Hyperlink Link { get; }

    public override AutomationControlType ControlType => AutomationControlType.Hyperlink;

    public override string Name => _block.LinkText(Link);

    public override string AutomationId => AutomationProperties.GetAutomationId(Link) ?? string.Empty;

    public override string HelpText => Link.NavigateUri?.ToString() ?? string.Empty;

    public override string ClassName => Link.GetType().Name;

    public override Rect BoundingRectangle => _blockPeer.LinkBounds(Link);

    public override bool IsEnabled => _blockPeer.IsEnabled;

    public override bool IsOffscreen => _blockPeer.IsOffscreen;

    public override bool HasKeyboardFocus => false;

    public override bool IsKeyboardFocusable => false;

    public override IReadOnlyList<AutomationPeer> GetChildren() => [];

    public override AutomationPeer GetParent() => _blockPeer;

    public override void SetFocus()
    {
    }

    public void Invoke() => Link.Activate();
}
