namespace Adamantium.XamlTests.MarkupFuzz;

internal sealed record MarkupMutation(string Kind, int Offset, string Text, string Element, string Attribute);
