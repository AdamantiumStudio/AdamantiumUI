using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Rendering.Payloads;

namespace Adamantium.UI.Rendering.RenderUnits;

/// <summary>Triangles with a color at each corner, drawn by their own unit.</summary>
public class ColoredMeshRenderUnit : RenderUnit<ColoredMeshPayload>
{
    public ColoredMeshRenderUnit(IDrawCommand command, RenderUnitContext context) : base(command, context)
    {
        Build();
    }

    public override void UpdateWithDrawCommand(IDrawCommand drawCommand)
    {
        if (drawCommand.Payload is not ColoredMeshPayload inputPayload)
        {
            return;
        }

        var rebuild = Payload.RequiresBufferRebuild(inputPayload);
        DrawCommand = drawCommand;
        Payload = inputPayload;
        if (rebuild)
        {
            GeometryRenderer?.DeferDispose();
            Build();
        }
        else if (GeometryRenderer != null)
        {
            GeometryRenderer.RenderData = drawCommand.RenderData;
        }
    }

    private void Build()
    {
        GeometryRenderer = new ColoredMeshRenderComponent(GraphicsDevice, UIBasicEffect, Payload.Mesh, Payload.Outline,
            BufferManager)
        {
            RenderData = DrawCommand.RenderData,
            Owner = DrawCommand.Component,
        };
    }
}
