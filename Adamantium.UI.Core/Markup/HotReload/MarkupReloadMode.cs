namespace Adamantium.UI.Core.Markup;

/// <summary>When a saved markup file is applied to the running application.</summary>
public enum MarkupReloadMode
{
    /// <summary>As soon as it is saved.</summary>
    OnSave,

    /// <summary>Only when asked, through <see cref="MarkupHotReload.ApplyAsync"/>; saves gather meanwhile.</summary>
    Manual,
}
