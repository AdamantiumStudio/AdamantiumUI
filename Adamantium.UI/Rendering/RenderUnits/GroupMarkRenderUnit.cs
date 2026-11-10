using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Rendering.Payloads;

namespace Adamantium.UI.Rendering.RenderUnits;

/// <summary>A mark in a group of draws: it draws nothing itself - the walk opens a target, or lays one down, where it
/// stands (see <see cref="GroupMarkKind"/>).</summary>
public sealed class GroupMarkRenderUnit : RenderUnit<GroupMarkPayload>
{
    public GroupMarkRenderUnit(IDrawCommand command, RenderUnitContext context) : base(command, context)
    {
    }

    public GroupMarkKind Kind => Payload.Kind;

    /// <summary>A begin mark: how the walk that recorded the frame put its group down, for the frames that replay it.
    /// </summary>
    internal CommandGroup Group { get; set; }

    public override void UpdateWithDrawCommand(IDrawCommand drawCommand)
    {
        if (drawCommand.Payload is not GroupMarkPayload payload) return;

        DrawCommand = drawCommand;
        Payload = payload;
    }

    public override bool Match(IDrawCommand drawCommand) =>
        drawCommand.Payload is GroupMarkPayload payload && payload.Kind == Kind;
}
