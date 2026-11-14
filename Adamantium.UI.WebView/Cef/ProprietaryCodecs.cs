using System.Runtime.InteropServices;

namespace Adamantium.UI.WebView.Cef;

/// <summary>H.264 and AAC in our CEF build: they sit behind Chromium's <see cref="Feature"/>, off by default, and the
/// software decoders live only in the FFmpeg with codecs, which takes the place of the one without them. The feature is
/// turned on only with that FFmpeg - otherwise pages are told H.264 and AAC play, while nothing decodes AAC and only a
/// GPU might decode H.264, and do not fall back to WebM.</summary>
internal static unsafe class ProprietaryCodecs
{
    private const int H264CodecId = 27;
    private const int AacCodecId = 0x15002;

    /// <summary>The Chromium feature our CEF build gates the patented codecs with.</summary>
    public const string Feature = "ProprietaryCodecs";

    /// <summary>Whether the FFmpeg in <paramref name="cefFolder"/> decodes H.264 and AAC - asked of FFmpeg itself, which
    /// libcef has loaded by then.</summary>
    public static bool IsInstalled(string cefFolder)
    {
        var file = Path.Combine(cefFolder, GetFFmpegFileName());
        if (!File.Exists(file) || !NativeLibrary.TryLoad(file, out var library))
        {
            return false;
        }

        try
        {
            if (!NativeLibrary.TryGetExport(library, "avcodec_find_decoder", out var export))
            {
                return false;
            }

            var findDecoder = (delegate* unmanaged<int, void*>)export;
            return findDecoder(H264CodecId) != null && findDecoder(AacCodecId) != null;
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static string GetFFmpegFileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "ffmpeg.dll";
        }

        return OperatingSystem.IsMacOS() ? "libffmpeg.dylib" : "libffmpeg.so";
    }
}
