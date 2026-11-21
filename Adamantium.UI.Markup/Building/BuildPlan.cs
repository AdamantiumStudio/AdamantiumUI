namespace Adamantium.UI.Markup.Building;

/// <summary>Everything one markup document builds: the body of its generated class's build method.</summary>
public sealed class BuildPlan : BuildScope
{
    /// <summary>The values made once for their places and kept for every build: members of the generated class, outside
    /// the body.</summary>
    public List<SharedValueStep> SharedValues { get; } = [];
}
