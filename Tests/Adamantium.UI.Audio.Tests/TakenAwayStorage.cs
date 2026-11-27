using Adamantium.Pulsar;

namespace Adamantium.UI.Audio.Tests;

/// <summary>A sound file in memory that can be taken away after it was listed.</summary>
internal sealed class TakenAwayStorage(byte[] file) : ISoundStorage
{
    public volatile bool IsTaken;

    public Stream Open() => IsTaken ? throw new IOException("Taken away.") : new MemoryStream(file);
}
