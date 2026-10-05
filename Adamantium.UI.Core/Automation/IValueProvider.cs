namespace Adamantium.UI.Core.Automation;

/// <summary>An element holding a value that reads and writes as text, such as a text box.</summary>
public interface IValueProvider
{
    string Value { get; }

    bool IsReadOnly { get; }

    void SetValue(string value);
}
