using System.Xml;
using Adamantium.UI.Markup.AST;

namespace Adamantium.UI.Markup;

public class LineInfo : IAumlLineInfo
{
    public LineInfo(IXmlLineInfo info)
    {
        Line = info.LineNumber;
        Position = info.LinePosition;
    }

    public LineInfo(int line, int position)
    {
        Line = line;
        Position = position;
    }
    public int Line { get; set; }
    public int Position { get; set; }

    public override string ToString()
    {
        return $"Line: {Line}, Position: {Position}";
    }
}