using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Rendering.Payloads;
using Adamantium.UI.Rendering.RenderUnits;

namespace Adamantium.UI.Rendering;

// A placed draw rides a transform slot of its own, holding the placement's matrix times its element's world: moving the
// placement is one slot write and a replay, never a record or a re-bake.
public partial class RenderCache
{
    private readonly List<Placement> _placementDirtyBuffer = new();

    // Placements whose matrix changed since the draw last wrote their slots.
    private readonly HashSet<Placement> _movedPlacements = new();

    // Placements moved this frame: per-unit draws riding them are re-pointed at replay.
    private readonly HashSet<Placement> _repointPlacements = new();

    // The placements whose slots each element holds, so they go back to the table when it stops drawing them.
    private readonly Dictionary<IUIComponent, HashSet<Placement>> _placementsByOwner = new();
    private readonly HashSet<Placement> _placementsKept = new();
    private readonly List<Placement> _placementsGone = new();

    private void CapturePlacements()
    {
        Dirty.SnapshotPlacementsInto(_placementDirtyBuffer);
        foreach (var placement in _placementDirtyBuffer)
        {
            _packet.Placements.Add((placement, placement.Matrix, placement.Version));
        }
    }

    private void ApplyPlacements(RenderPacket packet)
    {
        foreach (var (placement, matrix, version) in packet.Placements)
        {
            if (placement.Apply(matrix, version))
            {
                _movedPlacements.Add(placement);
            }
        }
    }

    // A unit built from a newer record than the last move knows the newer matrix.
    private static void CatchUp(IRenderUnit unit)
    {
        switch (unit)
        {
            case GeometryRenderUnit { Payload: { Placement: { } placement } payload }:
                placement.Apply(payload.PlacedAt, payload.PlacedVersion);
                break;
            case TextRenderUnit { Payload: { Placement: { } placement } payload }:
                placement.Apply(payload.PlacedAt, payload.PlacedVersion);
                break;
        }
    }

    private Matrix4x4F ResolveBake(IGraphicsDevice device, IRenderUnit unit, Matrix4x4F world, out int slot)
    {
        if (unit.Placement is not { } placement)
        {
            return ResolveBake(device, unit.Component, world, out slot);
        }

        CatchUp(unit);
        var component = unit.Component;
        slot = AcquireLiveSlot(device, placement.Id);
        _transformTable.SetMatrix(device, slot, placement.Applied * World(component));
        if (!_placementsByOwner.TryGetValue(component, out var held))
        {
            _placementsByOwner[component] = held = new HashSet<Placement>();
        }

        held.Add(placement);

        // A node's move rewrites the node's slot, not this one: the unit is carried as a straggler instead.
        if (NodeOf(component) != null)
        {
            MarkNodeNotAware(component);
        }

        return _cloneMatrix ?? Matrix4x4F.Identity;
    }

    // Writes the slots of the placements that moved; per-unit draws riding them are re-pointed at replay.
    private bool RefreshMovedPlacements(IGraphicsDevice device)
    {
        if (_movedPlacements.Count == 0 || _transformTable == null)
        {
            _movedPlacements.Clear();
            return true;
        }

        foreach (var placement in _movedPlacements)
        {
            if (placement.Owner is { } owner && _transformTable.TryGetSlot(placement.Id, out var slot))
            {
                _transformTable.SetMatrix(device, slot, placement.Applied * World(owner));
            }

            _repointPlacements.Add(placement);
        }

        _movedPlacements.Clear();
        return true;
    }

    // A walk bakes every placed unit from the placement's newest matrix itself.
    private void ForgetMovedPlacements() => _movedPlacements.Clear();

    // An element drew again: the slots of placements it no longer draws with go back to the table.
    private void ReconcilePlacements(IUIComponent component, IReadOnlyList<IDrawCommand> commands)
    {
        if (!_placementsByOwner.TryGetValue(component, out var held))
        {
            return;
        }

        _placementsKept.Clear();
        foreach (var command in commands)
        {
            switch (command.Payload)
            {
                case GeometryPayload { Placement: { } geometryPlacement }:
                    _placementsKept.Add(geometryPlacement);
                    break;
                case TextPayload { Placement: { } textPlacement }:
                    _placementsKept.Add(textPlacement);
                    break;
            }
        }

        _placementsGone.Clear();
        foreach (var placement in held)
        {
            if (!_placementsKept.Contains(placement))
            {
                _placementsGone.Add(placement);
            }
        }

        foreach (var placement in _placementsGone)
        {
            held.Remove(placement);
            _transformTable?.ReleaseSlot(placement.Id);
        }

        if (held.Count == 0)
        {
            _placementsByOwner.Remove(component);
        }

        _placementsKept.Clear();
        _placementsGone.Clear();
    }

    private void ReleasePlacements(IUIComponent component)
    {
        if (!_placementsByOwner.Remove(component, out var held))
        {
            return;
        }

        foreach (var placement in held)
        {
            _transformTable?.ReleaseSlot(placement.Id);
        }
    }

    private void ReleaseAllPlacements() => _placementsByOwner.Clear();
}
