using System;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;
using Buffer = Adamantium.Graphics.Buffer;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>Bins one flush's curve fills into 16x16 pixel screen tiles on the GPU (CurveTileEffect.fx), so a fill's
/// fragment walks only the pieces near it, however large or dense the fill. Each flush keeps its own: binned before its
/// draws and outside the render pass when its fills change, drawn from as they are when they do not.</summary>
internal sealed class CurveTiler : DeferredDisposableObject
{
    private const uint GroupSize = 64;
    private const BufferUsageFlags Usage = BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress;
    private const MemoryPropertyFlags Readback = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent;

    // The fewest list entries and tiles kept; more as the flush is seen to need them.
    private const uint LeastCapacity = 1024;
    private const uint LeastTiles = 64;

    private readonly IGraphicsDevice _device;
    private readonly CurveTileEffect _effect;

    // The scratch the passes write and the fragments read: the GPU alone touches it, in submission order, so one set
    // serves every flush - each starts behind a barrier on the last one's reads.
    private Buffer _counts;
    private Buffer _cursors;
    private Buffer _offsets;
    private Buffer _backdrops;
    private Buffer _entries;
    private uint _tileCapacity;
    private uint _capacity;

    // How many entries a flush placed, one copy per frame in flight, read back once its frame has finished.
    private Buffer<uint>[] _counters = [];
    private bool[] _countersUsed = [];

    // The items the tiles were last binned for, one copy per frame in flight: a run writes the copy after the one frames
    // still in flight read, and a flush that holds the same items draws from the one written.
    private Buffer<CurveTileItem>[] _itemCopies = [];
    private int _itemCopy;
    private ulong _items;

    // What the last run binned, and when - its tiles are only trusted once its count is back and fitted.
    private CurveTileItem[] _held = [];
    private int _heldCount;
    private uint _heldPieces;
    private uint _heldTiles;
    private uint _heldRows;
    private uint _heldFrame;
    private int _heldCounter = -1;

    /// <summary>A flush's tiles, binned with <paramref name="effect"/> - shared by every flush, and not this one's to
    /// dispose.</summary>
    public CurveTiler(IGraphicsDevice device, CurveTileEffect effect) : base(device)
    {
        _device = device;
        _effect = effect;
    }

    /// <summary>Whether the tiles already hold exactly <paramref name="items"/> - a clean frame of an unchanged scene -
    /// so the flush draws from them without binning again.</summary>
    public bool Holds(ReadOnlySpan<CurveTileItem> items, uint pieces, uint tiles, uint rows)
    {
        if (_heldCounter < 0 || items.Length != _heldCount || pieces != _heldPieces || tiles != _heldTiles ||
            rows != _heldRows)
        {
            return false;
        }

        // Its count can be read once its frame has surely finished: a run that found the lists too small bins again, with
        // them grown.
        if (_device.CurrentFrame - _heldFrame >= Math.Max(1u, _device.MaxFramesInFlight) &&
            _counters[_heldCounter].GetData<uint>()[0] > _capacity)
        {
            return false;
        }

        return System.Runtime.InteropServices.MemoryMarshal.AsBytes(items)
            .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(_held.AsSpan(0, _heldCount)));
    }

    /// <summary>Bins <paramref name="items"/>: <paramref name="pieces"/>, <paramref name="tiles"/> and
    /// <paramref name="rows"/> are their totals. Recorded into the current command buffer, outside any render pass.
    /// </summary>
    public void Run(ReadOnlySpan<CurveTileItem> items, uint pieces, uint tiles, uint rows)
    {
        var counter = TakeCounter(pieces);
        EnsureScratch(tiles);
        _items = WriteItems(items);
        Hold(items, pieces, tiles, rows);

        _effect.CurveTileItems.SetValue(_items);
        _effect.CurveTileCounts.SetValue(_counts.GetDeviceAddress());
        _effect.CurveTileCursors.SetValue(_cursors.GetDeviceAddress());
        _effect.CurveTileOffsets.SetValue(_offsets.GetDeviceAddress());
        _effect.CurveTileBackdrops.SetValue(_backdrops.GetDeviceAddress());
        _effect.CurveTileEntries.SetValue(_entries.GetDeviceAddress());
        _effect.CurveTileCounter.SetValue(counter.GetDeviceAddress());
        _effect.CurveTileCapacity.SetValue(_capacity);
        _effect.CurveTileItemCount.SetValue((uint)items.Length);
        _effect.CurveTilePieceTotal.SetValue(pieces);
        _effect.CurveTileTotal.SetValue(tiles);
        _effect.CurveTileRowTotal.SetValue(rows);

        BarrierAll(counter, PipelineStageFlagBits2.FragmentShaderBit | PipelineStageFlagBits2.ComputeShaderBit,
            PipelineStageFlagBits2.ComputeShaderBit);
        Dispatch(_effect.CurveTileClearPass, Math.Max(tiles, 1u));
        BarrierAll(counter, PipelineStageFlagBits2.ComputeShaderBit, PipelineStageFlagBits2.ComputeShaderBit);
        Dispatch(_effect.CurveTileCountPass, pieces);
        BarrierAll(counter, PipelineStageFlagBits2.ComputeShaderBit, PipelineStageFlagBits2.ComputeShaderBit);
        Dispatch(_effect.CurveTileAllocatePass, tiles);
        Dispatch(_effect.CurveTileBackdropPass, rows * 16u);
        BarrierAll(counter, PipelineStageFlagBits2.ComputeShaderBit, PipelineStageFlagBits2.ComputeShaderBit);
        Dispatch(_effect.CurveTileFillPass, pieces);
        BarrierAll(counter, PipelineStageFlagBits2.ComputeShaderBit, PipelineStageFlagBits2.FragmentShaderBit);
    }

    /// <summary>Hands the last run's tiles to an effect whose fill passes read them (see CurveTiles.fxh).</summary>
    public void ApplyTo(Effect effect)
    {
        var parameters = effect.Parameters;
        parameters["CurveTileItems"].SetValue(_items);
        parameters["CurveTileCounts"].SetValue(_counts.GetDeviceAddress());
        parameters["CurveTileOffsets"].SetValue(_offsets.GetDeviceAddress());
        parameters["CurveTileBackdrops"].SetValue(_backdrops.GetDeviceAddress());
        parameters["CurveTileEntries"].SetValue(_entries.GetDeviceAddress());
        parameters["CurveTileCapacity"].SetValue(_capacity);
    }

    private ulong WriteItems(ReadOnlySpan<CurveTileItem> items)
    {
        var copies = (int)Math.Max(1u, _device.MaxFramesInFlight) + 1;
        if (_itemCopies.Length != copies)
        {
            foreach (var old in _itemCopies) Release(old);
            _itemCopies = new Buffer<CurveTileItem>[copies];
        }

        _itemCopy = (_itemCopy + 1) % copies;
        ref var copy = ref _itemCopies[_itemCopy];
        if (copy == null || copy.ElementCount < (ulong)items.Length)
        {
            Release(copy);
            copy = Buffer.New<CurveTileItem>(_device, (ulong)Math.Max(64, NextPowerOfTwo((uint)items.Length)), Usage,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);
        }

        copy.SetData(items);
        return copy.GetDeviceAddress();
    }

    private void Hold(ReadOnlySpan<CurveTileItem> items, uint pieces, uint tiles, uint rows)
    {
        if (_held.Length < items.Length) _held = new CurveTileItem[NextPowerOfTwo((uint)items.Length)];
        items.CopyTo(_held);
        _heldCount = items.Length;
        _heldPieces = pieces;
        _heldTiles = tiles;
        _heldRows = rows;
        _heldFrame = _device.CurrentFrame;
        _heldCounter = (int)(_device.CurrentFrame % Math.Max(1u, _device.MaxFramesInFlight));
    }

    private void Dispatch(IEffectPass pass, uint threads)
    {
        if (threads == 0) return;

        pass.Apply();
        _device.Dispatch((threads + GroupSize - 1) / GroupSize);
    }

    private void BarrierAll(Buffer counter, PipelineStageFlagBits2 from, PipelineStageFlagBits2 to)
    {
        Barrier(_counts, from, to);
        Barrier(_cursors, from, to);
        Barrier(_offsets, from, to);
        Barrier(_backdrops, from, to);
        Barrier(_entries, from, to);
        Barrier((Buffer)counter, from, to);
    }

    private void Barrier(Buffer buffer, PipelineStageFlagBits2 from, PipelineStageFlagBits2 to)
    {
        const AccessFlagBits2 access = AccessFlagBits2.ShaderReadBit | AccessFlagBits2.ShaderWriteBit;
        _device.BufferBarrier(buffer, from, access, to, access);
    }

    // This frame's counter: the copy it last used finished a lap of frames ago, so what it counted is read now - and a
    // flush that found the lists too small has them grown from here on.
    private Buffer<uint> TakeCounter(uint pieces)
    {
        var copies = (int)Math.Max(1u, _device.MaxFramesInFlight);
        if (_counters.Length != copies)
        {
            foreach (var old in _counters) _device.AddToDeferDisposeQueue(old);
            _counters = new Buffer<uint>[copies];
            _countersUsed = new bool[copies];
        }

        var slot = (int)(_device.CurrentFrame % (uint)copies);

        _counters[slot] ??= Buffer.New<uint>(_device, 1ul, Usage, Readback);
        var counter = _counters[slot];
        var wanted = Math.Max(LeastCapacity, pieces * 4u);
        if (_countersUsed[slot])
        {
            var placed = counter.GetData<uint>()[0];
            if (placed > _capacity) wanted = Math.Max(wanted, placed + placed / 4u);
        }

        _countersUsed[slot] = true;
        if (wanted > _capacity)
        {
            Replace(ref _entries, wanted);
            _capacity = wanted;
        }

        return counter;
    }

    private void EnsureScratch(uint tiles)
    {
        if (_counts != null && tiles <= _tileCapacity) return;

        var capacity = Math.Max(LeastTiles, NextPowerOfTwo(tiles));
        Replace(ref _counts, capacity);
        Replace(ref _cursors, capacity);
        Replace(ref _offsets, capacity);
        Replace(ref _backdrops, capacity * 16u);
        _tileCapacity = capacity;
    }

    private void Replace(ref Buffer buffer, uint words)
    {
        if (buffer != null) _device.AddToDeferDisposeQueue(buffer);
        buffer = Buffer.New<uint>(_device, words, Usage, MemoryPropertyFlags.DeviceLocal);
    }

    private static uint NextPowerOfTwo(uint value)
    {
        var power = 1u;
        while (power < value) power <<= 1;
        return power;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        if (disposeManagedResources)
        {
            Release(_counts);
            Release(_cursors);
            Release(_offsets);
            Release(_backdrops);
            Release(_entries);
            foreach (var counter in _counters) Release(counter);
            foreach (var copy in _itemCopies) Release(copy);
        }

        base.Dispose(disposeManagedResources);
    }

    private void Release(Buffer buffer)
    {
        if (buffer != null) _device.AddToDeferDisposeQueue(buffer);
    }
}
