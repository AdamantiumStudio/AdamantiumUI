using Adamantium.Mathematics.Svg;

namespace Adamantium.UI.Core.Media;

public class SVGCommand
{
    public SVGCommand(char command)
    {
        Command = command;
        Arguments = new List<double>();
    }

    /// <summary>A command of path data as the engine reads it (<see cref="SvgPathData"/>).</summary>
    public SVGCommand(SvgPathCommand command) : this(command.Letter)
    {
        Arguments.AddRange(command.Arguments);
    }

    public char Command { get; }

    public List<double> Arguments { get; }

    /// <summary>One command and its arguments, its numbers read as <see cref="SvgPathData.ReadNumbers"/> reads them.</summary>
    public static SVGCommand Parse(string svgCommand)
    {
        var command = new SVGCommand(svgCommand[0]);
        command.Arguments.AddRange(SvgPathData.ReadNumbers(svgCommand.Substring(1)));
        return command;
    }

    public override string ToString()
    {
        return $"Command: {Command}, Params: {string.Join(',', Arguments)}";
    }
}
