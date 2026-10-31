using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Resources;

namespace Adamantium.XamlTests;

/// <summary>A dictionary file a theme's resources link, carrying one style without a key for texts.</summary>
internal sealed class ThemedTexts : ResourceDictionary
{
    public ThemedTexts()
    {
        Styles.Add(TestStyles.Create(typeof(TextBlock), "Tag", "linked"));
    }
}
