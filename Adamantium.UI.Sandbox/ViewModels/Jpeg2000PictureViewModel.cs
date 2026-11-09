using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Adamantium.Core.TypeParsing;
using Adamantium.Imaging;
using Adamantium.Imaging.Jpeg2000;
using Adamantium.MVVM;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>One JPEG 2000 picture in the gallery, shown at the resolution and quality its sliders choose. The image
/// reports how many of each its file has; what the chosen part costs - its size, the bytes it is decoded from, the
/// time - is measured here by decoding that part again.</summary>
public partial class Jpeg2000PictureViewModel : AdamantiumViewModel
{
    private readonly string _path;
    private int _measuring;

    public Jpeg2000PictureViewModel(string title, string path)
    {
        Title = title;
        _path = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        Source = TypeParser.Parse<ImageSource>(path);
    }

    public string Title { get; }

    public ImageSource Source { get; }

    [Bindable] private uint _mipLevelCount;
    [Bindable] private uint _qualityLevelCount;
    [Bindable] private uint _mipLevel;
    [Bindable] private uint _qualityLevel;
    [Bindable] private int _decodedWidth;
    [Bindable] private int _decodedHeight;
    [Bindable] private string _usedKilobytes = "";
    [Bindable] private string _totalKilobytes = "";
    [Bindable] private long _milliseconds;

    public uint LastMipLevel => MipLevelCount > 0 ? MipLevelCount - 1 : 0;

    public uint LastQualityLevel => QualityLevelCount > 0 ? QualityLevelCount - 1 : 0;

    /// <summary>The layers decoded at the chosen quality, the first one counted.</summary>
    public uint LayersShown => QualityLevelCount > QualityLevel ? QualityLevelCount - QualityLevel : 0;

    partial void OnMipLevelCountChanged(uint value) => RaisePropertyChanged(nameof(LastMipLevel));

    partial void OnQualityLevelCountChanged(uint value)
    {
        RaisePropertyChanged(nameof(LastQualityLevel));
        RaisePropertyChanged(nameof(LayersShown));
        Measure();
    }

    partial void OnMipLevelChanged(uint value) => Measure();

    partial void OnQualityLevelChanged(uint value)
    {
        RaisePropertyChanged(nameof(LayersShown));
        Measure();
    }

    private async void Measure()
    {
        if (QualityLevelCount == 0)
        {
            return;
        }

        var ticket = ++_measuring;
        var options = new Jpeg2000Options
        {
            DiscardLevels = (int)MipLevel,
            Layers = QualityLevel == 0 ? 0 : (int)LayersShown,
        };
        try
        {
            var (image, elapsed) = await Task.Run(() =>
            {
                var data = File.ReadAllBytes(_path);
                var watch = Stopwatch.StartNew();
                var decoded = Jpeg2000Decoder.Decode(data, BitmapLoader.MaxPixels, options);
                return (decoded, watch.ElapsedMilliseconds);
            });
            if (ticket != _measuring)
            {
                return;
            }

            DecodedWidth = image.Width;
            DecodedHeight = image.Height;
            UsedKilobytes = (image.CodedBytesUsed / 1024.0).ToString("0.0");
            TotalKilobytes = (image.CodedBytes / 1024.0).ToString("0.0");
            Milliseconds = elapsed;
        }
        catch (Exception)
        {
            if (ticket != _measuring)
            {
                return;
            }

            DecodedWidth = DecodedHeight = 0;
            UsedKilobytes = TotalKilobytes = "-";
            Milliseconds = 0;
        }
    }
}
