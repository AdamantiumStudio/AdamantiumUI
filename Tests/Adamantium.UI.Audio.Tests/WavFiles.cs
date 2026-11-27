using System.Buffers.Binary;
using System.Text;

namespace Adamantium.UI.Audio.Tests;

/// <summary>WAV files made here: a quiet tone of 48 kHz, of one channel or two, with a title.</summary>
internal static class WavFiles
{
    public static byte[] Tone(double seconds, int channels = 2, string title = null)
    {
        const int rate = 48000;
        var frames = (int)(rate * seconds);
        var data = new byte[frames * channels * 4];
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = (float)(0.25 * Math.Sin(2 * Math.PI * 440 * frame / rate));
            for (var channel = 0; channel < channels; channel++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((frame * channels + channel) * 4), sample);
            }
        }

        var info = title == null ? [] : InfoOf(title);
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write("RIFF"u8);
        writer.Write(4 + 8 + 16 + info.Length + 8 + data.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)3);
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * channels * 4);
        writer.Write((short)(channels * 4));
        writer.Write((short)32);
        writer.Write(info);
        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
        writer.Flush();
        return file.ToArray();
    }

    // LIST INFO with INAM: the title, zero-ended, padded to even.
    private static byte[] InfoOf(string title)
    {
        var text = Encoding.UTF8.GetBytes(title + "\0");
        var padded = text.Length + (text.Length & 1);
        var list = new byte[8 + 4 + 8 + padded];
        "LIST"u8.CopyTo(list);
        BinaryPrimitives.WriteInt32LittleEndian(list.AsSpan(4), list.Length - 8);
        "INFO"u8.CopyTo(list.AsSpan(8));
        "INAM"u8.CopyTo(list.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(list.AsSpan(16), text.Length);
        text.CopyTo(list.AsSpan(20));
        return list;
    }
}
