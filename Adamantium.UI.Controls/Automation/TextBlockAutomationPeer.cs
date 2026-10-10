using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="TextBlock"/>: text, called by what it says; the links in it and the controls set
/// into its lines are its children.</summary>
public class TextBlockAutomationPeer : UIComponentAutomationPeer
{
    private readonly ConditionalWeakTable<Hyperlink, HyperlinkAutomationPeer> _linkPeers = new();

    public TextBlockAutomationPeer(TextBlock owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Text;

    protected override string NameCore() => ((TextBlock)Owner).ShownText;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var block = (TextBlock)Owner;
        var links = block.Links;
        if (links.Count == 0)
        {
            return block.HostedChildren.Count == 0 ? [] : base.ChildrenCore();
        }

        var peers = new List<AutomationPeer>(links.Count);
        foreach (var link in links)
        {
            peers.Add(_linkPeers.GetValue(link, key => new HyperlinkAutomationPeer(this, block, key)));
        }

        if (block.HostedChildren.Count > 0)
        {
            peers.AddRange(base.ChildrenCore());
        }

        return peers;
    }

    internal Rect LinkBounds(Hyperlink link)
    {
        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        foreach (var rect in ((TextBlock)Owner).LinkRects(link))
        {
            var screen = ScreenRect((TextBlock)Owner, rect);
            if (screen.IsEmpty)
            {
                continue;
            }

            left = Math.Min(left, screen.X);
            top = Math.Min(top, screen.Y);
            right = Math.Max(right, screen.X + screen.Width);
            bottom = Math.Max(bottom, screen.Y + screen.Height);
        }

        return left > right ? Rect.Empty : new Rect(left, top, right - left, bottom - top);
    }
}
