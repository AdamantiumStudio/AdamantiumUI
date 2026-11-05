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

    // The newest matrix this cache has heard of for each placement, and what its slots and units show: the second takes
    // the first only where a frame may write a slot - the replay and patch chains, or a walk.
    private readonly Dictionary<Placement, (Matrix4x4F Matrix, long Version)> _placementLatest = new();
    private readonly Dictionary<Placement, Matrix4x4F> _placementShown = new();

    // Placements whose newest matrix is not shown yet.
    private readonly HashSet<Placement> _movedPlacements = new();

    // Placements moved this frame: per-unit draws riding them are re-pointed at replay.
    private readonly HashSet<Placement> _repointPlacements = new();

    // The placements whose slots each element holds, so they go back to the table when it stops drawing them.
    private readonly Dictionary<IUIComponent, HashSet<Placement>> _placementsByOwner = new();
    private readonly HashSet<Placement> _placementsKept = new();
    private readonly List<Placement> _placementsGone = new();
    private readonly List<IUIComponent> _placementOwnersMoved = new();

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
            // Only what this cache draws: a placement drawn for the first time brings its matrix in its record.
            if (placement.Owner is not { } owner || !_placementsByOwner.TryGetValue(owner, out var held)
                || !held.Contains(placement))
            {
                continue;
            }

            if (Hear(placement, matrix, version))
            {
                _movedPlacements.Add(placement);
            }
        }
    }

    private bool Hear(Placement placement, in Matrix4x4F matrix, long version)
    {
        if (_placementLatest.TryGetValue(placement, out var known) && known.Version >= version)
        {
            return false;
        }

        _placementLatest[placement] = (matrix, version);
        return true;
    }

    // What the placement's slots and units show; a placement never shown yet shows its newest matrix.
    private Matrix4x4F Shown(Placement placement)
    {
        if (_placementShown.TryGetValue(placement, out var shown))
        {
            return shown;
        }

        shown = _placementLatest.TryGetValue(placement, out var latest) ? latest.Matrix : placement.Matrix;
        _placementShown[placement] = shown;
        return shown;
    }

    // The newest matrix this cache knows for the placement - what a drawing made now must show.
    private Matrix4x4F Newest(Placement placement) =>
        _placementLatest.TryGetValue(placement, out var latest) ? latest.Matrix : Shown(placement);

    // A unit built from a newer record than the last move knows the newer matrix; the unit then takes what is shown, and
    // its element is known to draw with the placement.
    private void CatchUp(IRenderUnit unit)
    {
        switch (unit)
        {
            case GeometryRenderUnit { Payload: { Placement: { } placement } payload } geometry:
                Hear(placement, payload.PlacedAt, payload.PlacedVersion);
                geometry.PlacedMatrix = Shown(placement);
                Hold(unit.Component, placement);
                break;
            case TextRenderUnit { Payload: { Placement: { } placement } payload } text:
                Hear(placement, payload.PlacedAt, payload.PlacedVersion);
                text.PlacedMatrix = Shown(placement);
                Hold(unit.Component, placement);
                break;
        }
    }

    private void Hold(IUIComponent component, Placement placement)
    {
        if (component == null)
        {
            return;
        }

        if (!_placementsByOwner.TryGetValue(component, out var held))
        {
            _placementsByOwner[component] = held = new HashSet<Placement>();
        }

        held.Add(placement);
    }

    private Matrix4x4F ResolveBake(IGraphicsDevice device, IRenderUnit unit, Matrix4x4F world, out int slot)
    {
        if (unit.Placement is not { } placement)
        {
            return ResolveBake(device, unit.Component, world, out slot);
        }

        CatchUp(unit);
        slot = AcquireLiveSlot(device, placement.Id);
        _transformTable.SetMatrix(device, slot, Shown(placement) * World(unit.Component));
        return _cloneMatrix ?? Matrix4x4F.Identity;
    }

    // Shows the newest matrices of the placements that moved: their slots are written, and the per-unit draws riding
    // them are re-pointed at replay.
    private bool RefreshMovedPlacements(IGraphicsDevice device)
    {
        if (_movedPlacements.Count == 0)
        {
            return true;
        }

        foreach (var placement in _movedPlacements)
        {
            _placementShown[placement] = _placementLatest[placement].Matrix;
            if (_transformTable != null && placement.Owner is { } owner
                && _transformTable.TryGetSlot(placement.Id, out var slot))
            {
                _transformTable.SetMatrix(device, slot, _placementShown[placement] * World(owner));
            }

            _repointPlacements.Add(placement);
        }

        _movedPlacements.Clear();
        return true;
    }

    // A walk bakes every placed unit itself, from the newest matrices.
    private void ShowNewestPlacements()
    {
        foreach (var placement in _movedPlacements)
        {
            _placementShown[placement] = _placementLatest[placement].Matrix;
        }

        _movedPlacements.Clear();
    }

    // Elements moved by a node or the compositor carry their placed drawings' slots along: each slot holds its own
    // world, which the move just changed.
    private void CarryPlacementsUnder(IGraphicsDevice device, List<IUIComponent> roots)
    {
        if (_placementsByOwner.Count == 0 || roots.Count == 0 || _transformTable == null)
        {
            return;
        }

        _placementOwnersMoved.Clear();
        foreach (var owner in _placementsByOwner.Keys)
        {
            if (IsUnder(owner, roots))
            {
                _placementOwnersMoved.Add(owner);
            }
        }

        foreach (var owner in _placementOwnersMoved)
        {
            var world = World(owner);
            foreach (var placement in _placementsByOwner[owner])
            {
                if (_transformTable.TryGetSlot(placement.Id, out var slot))
                {
                    _transformTable.SetMatrix(device, slot, Shown(placement) * world);
                }
            }
        }

        _placementOwnersMoved.Clear();
    }

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
            Forget(placement);
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
            Forget(placement);
        }
    }

    private void Forget(Placement placement)
    {
        _transformTable?.ReleaseSlot(placement.Id);
        _placementLatest.Remove(placement);
        _placementShown.Remove(placement);
        _movedPlacements.Remove(placement);
    }

    private void ReleaseAllPlacements()
    {
        _placementsByOwner.Clear();
        _placementLatest.Clear();
        _placementShown.Clear();
        _movedPlacements.Clear();
        _repointPlacements.Clear();
    }

    /// <summary>Whether <paramref name="slotOwner"/> names a placement <paramref name="component"/> draws with - the
    /// owner of a placed drawing's transform slot.</summary>
    internal bool IsPlacementOf(IUIComponent component, System.Guid slotOwner)
    {
        if (component == null || !_placementsByOwner.TryGetValue(component, out var held))
        {
            return false;
        }

        foreach (var placement in held)
        {
            if (placement.Id == slotOwner)
            {
                return true;
            }
        }

        return false;
    }
}
