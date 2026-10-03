namespace Adamantium.UI.Sandbox;

/// <summary>Numbers the Markup tab reads straight out of C# with <c>{x:Static}</c>. They live here, once - the point of
/// the directive is that markup does not have to restate them as resources to be able to say them.</summary>
public static class DemoMetrics
{
    public const double SwatchWidth = 240;

    public const double SwatchHeight = 64;

    public const double RowWidth = 460;

    /// <summary>Said in the language the page is opened in: {x:Static} reads a value once.</summary>
    public static string Caption => Localization.MarkupStrings.StaticCaption;
}
