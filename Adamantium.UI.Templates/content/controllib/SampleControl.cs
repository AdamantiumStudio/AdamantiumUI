using Adamantium.UI.Controls.Base;
using AdamantiumControlLibrary.Themes;

namespace AdamantiumControlLibrary;

/// <summary>A templated control: its look is the template in Themes/SampleControlStyleSet.auml.</summary>
public class SampleControl : Control
{
    // Before the first one is made, the library's style set goes into every theme, so the control has its look whichever
    // theme is current or switched to. Every control of the library can do the same: the set is added once.
    static SampleControl()
    {
        AddStyleSetToThemes<SampleControlStyleSet>();
    }
}
