using System.Text.RegularExpressions;
using System.Xml;

namespace Adamantium.UI.Markup.Parsers;

/// <summary>What is wrong with markup that is not well-formed XML.</summary>
public static class XmlProblem
{
    private static readonly Regex Position = new(@"\s*Line \d+, position \d+\.\s*$", RegexOptions.Compiled);

    /// <summary>The parser's message without the line and position it ends with - the place is given apart.</summary>
    public static string MessageOf(XmlException exception) => Position.Replace(exception.Message, string.Empty);
}
