using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Resources;

namespace Adamantium.XamlTests;

/// <summary>A dictionary file carrying one style without a key: texts 42 wide.</summary>
internal sealed class WideTexts : ResourceDictionary
{
    public WideTexts()
    {
        Styles.Add(TestStyles.Create(typeof(TextBlock), "Width", 42.0));
    }
}
