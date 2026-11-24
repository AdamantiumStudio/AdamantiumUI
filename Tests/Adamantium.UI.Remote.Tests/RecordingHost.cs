using System.Collections.Concurrent;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Remote;

namespace Adamantium.UI.Remote.Tests;

/// <summary>A universe host that keeps what its viewer gives it, for a test to wait on.</summary>
public sealed class RecordingHost : IUniverseHostService
{
    private int _newSurfacesAsked;

    public BlockingCollection<UniverseLaunch> Launches { get; } = new();

    public BlockingCollection<string> Sizes { get; } = new();

    public BlockingCollection<bool> Focuses { get; } = new();

    public BlockingCollection<KeyboardInput> Keys { get; } = new();

    public BlockingCollection<MouseInput> Mouse { get; } = new();

    public BlockingCollection<string> Pointer { get; } = new();

    public BlockingCollection<ulong> AskedFrames { get; } = new();

    public Task LaunchAsync(UniverseLaunch launch, CancellationToken cancellationToken)
    {
        Launches.Add(launch);
        return Task.CompletedTask;
    }

    public void Resize(uint width, uint height, float pixelsPerPoint) => Sizes.Add($"{width}x{height}@{pixelsPerPoint}");

    public void AskFrame(ulong frame) => AskedFrames.Add(frame);

    public int NewSurfacesAsked => Volatile.Read(ref _newSurfacesAsked);

    public void AskNewSurface() => Interlocked.Increment(ref _newSurfacesAsked);

    public void DrawWhereItStands(bool heldAtAStatement)
    {
    }

    public Task GoOnAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void MovePointer(float x, float y, bool isOver) => Pointer.Add($"{x},{y},{isOver}");

    public void Focus(bool hasKeyboard) => Focuses.Add(hasKeyboard);

    public void TakeKeyboardInput(KeyboardInput input) => Keys.Add(input);

    public void TakeMouseInput(MouseInput input) => Mouse.Add(input);

    public void TakeGestureInput(GestureInput input)
    {
    }

    public void TakeText(string text)
    {
    }

    public Task<string> CaptureSceneAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);

    public Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
