using Adamantium.UI.Markup.Building;

namespace Adamantium.UI.Core.Markup;

internal sealed class BuildFunction(FunctionStep step, BuildFrame frame)
{
    public FunctionStep Step { get; } = step;

    public BuildFrame Frame { get; } = frame;
}
