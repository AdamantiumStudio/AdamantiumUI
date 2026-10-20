using System;
using Adamantium.Graphics.Core;
using Adamantium.UI.Rendering.Retained;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

internal enum RecordedReplay
{
    Issued,
    Recorded,
    Executed
}

public partial class RenderCache
{
    internal static bool RecordedReplayDisabled = Environment.GetEnvironmentVariable("ADAM_NO_RECORDED_REPLAY") == "1";

    private IRecordedCommands[] _recorded = [];
    private ulong[] _recordedHash = [];
    private int _recordMisses;
    private int _recordCooldown;

    internal RecordedReplay LastRecordedReplay { get; private set; }

    private void ExecuteOpsRecorded(IGraphicsDevice device, Rect2D fullScissor)
    {
        if (RecordedReplayDisabled || !device.SupportsRecordedCommands || !OpsRecordable())
        {
            Issue(device, fullScissor);
            return;
        }

        var slots = (int)Math.Max(1, device.MaxFramesInFlight);
        if (_recorded.Length != slots)
        {
            DisposeRecordings();
            _recorded = new IRecordedCommands[slots];
            _recordedHash = new ulong[slots];
        }

        if (_recordCooldown > 0)
        {
            _recordCooldown--;
            Issue(device, fullScissor);
            return;
        }

        var slot = (int)(device.CurrentFrame % (uint)slots);
        var recorded = _recorded[slot];
        if (recorded is { IsReusable: true } && _recordedHash[slot] != 0)
        {
            device.BeginFingerprint();
            ulong fingerprint;
            try
            {
                ExecuteOps(device, fullScissor);
            }
            finally
            {
                fingerprint = device.EndFingerprint();
            }

            if (fingerprint == _recordedHash[slot])
            {
                device.ExecuteRecorded(recorded);
                _recordMisses = 0;
                LastRecordedReplay = RecordedReplay.Executed;
                return;
            }

            ForgetRecordedHashes();
            _recordMisses = Math.Min(_recordMisses + 1, 5);
            _recordCooldown = _recordMisses < 2 ? 0 : 1 << _recordMisses;
            device.SetScissors(fullScissor);
        }

        var recording = device.BeginRecording(recorded);
        if (!ReferenceEquals(recording, recorded)) recorded?.Dispose();
        _recorded[slot] = recording;
        var finished = false;
        try
        {
            ExecuteOps(device, fullScissor);
            finished = true;
        }
        finally
        {
            var hash = device.EndRecording();
            _recordedHash[slot] = finished ? hash : 0;
        }

        device.ExecuteRecorded(_recorded[slot]);
        LastRecordedReplay = RecordedReplay.Recorded;
    }

    private void Issue(IGraphicsDevice device, Rect2D fullScissor)
    {
        ExecuteOps(device, fullScissor);
        LastRecordedReplay = RecordedReplay.Issued;
    }

    private bool OpsRecordable()
    {
        foreach (var op in _ops)
        {
            if (op.Kind == RenderOpKind.LayerBegin
                || (op.Kind == RenderOpKind.Segment && op.Batch == 13)
                || (op.Kind == RenderOpKind.InstancedFlush && _instancedFill.FlushDrawsMaterials(op.SegId)))
            {
                return false;
            }
        }

        return true;
    }

    private void ForgetRecordedHashes() => Array.Clear(_recordedHash);

    private void DisposeRecordings()
    {
        foreach (var recorded in _recorded)
        {
            recorded?.Dispose();
        }

        _recorded = [];
        _recordedHash = [];
    }
}
