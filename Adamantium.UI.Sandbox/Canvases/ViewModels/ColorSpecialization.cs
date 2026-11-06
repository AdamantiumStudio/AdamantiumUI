using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.UI.Controls.Canvases;

namespace Adamantium.UI.Sandbox.Canvases.ViewModels;

/// <summary>A COLOR somebody picked - where color enters the graph.</summary>
public partial class ColorSpecialization : NodeSpecialization
{
    [Bindable] private Color _color = Color.FromRgba(80, 140, 220, 255);

    public override ValueTask<object> Evaluate(IReadOnlyList<CanvasArrival> inputs, CancellationToken token) =>
        new(Color);
}
