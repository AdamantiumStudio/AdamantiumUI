using Adamantium.Graphics.Fonts;
using Adamantium.MVVM;

namespace Adamantium.UI.Sandbox.ViewModels;

// The "Text on a path" topic: one text, one look, on every path of the topic at once.
public partial class TextViewModel
{
    [Bindable] private string _pathText = "Текст по кривой · Text along a curve";

    [Bindable] private double _pathFontSize = 20;

    [Bindable] private TextPathAlignment _pathAlignment = TextPathAlignment.Center;

    [Bindable] private TextPathOverflow _pathOverflow = TextPathOverflow.Clip;

    [Bindable] private double _pathStartOffset;

    [Bindable] private double _pathAcross;

    [Bindable] private double _pathMaxTurn = 180;

    [Bindable] private bool _pathKeepUpright = true;

    [Bindable] private bool _pathShowLine = true;

    [Bindable] private double _pathLineThickness = 1.5;

    partial void OnPathShowLineChanged(bool value) => PathLineThickness = value ? 1.5 : 0;
}
