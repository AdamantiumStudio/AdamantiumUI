using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="Hyperlink"/> inside a <see cref="TextBlock"/>: a link, called by its text, invoked
/// as a click on it.</summary>
public class HyperlinkAutomationPeer : AutomationPeer, IInvokeProvider, IElementPartPeer
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

    public override bool HasKeyboardFocus => _block.IsKeyboardFocused && ReferenceEquals(_block.FocusedLink, Link);

    public override bool IsKeyboardFocusable => _block.Focusable;

    public override IReadOnlyList<AutomationPeer> GetChildren() => [];

    public override AutomationPeer GetParent() => _blockPeer;

    public override void SetFocus() => _block.FocusLink(Link);

    public void Invoke() => Link.Activate();

    public UIComponent Element => _block;

    /// <summary>The middle of the link's first piece: a link broken across lines is clicked on its first line.</summary>
    public Vector2? Middle => _block.LinkRects(Link) is { Count: > 0 } rects
        ? new Vector2(rects[0].X + rects[0].Width / 2, rects[0].Y + rects[0].Height / 2)
        : null;
}
