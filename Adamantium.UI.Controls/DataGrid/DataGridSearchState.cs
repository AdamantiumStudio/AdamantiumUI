namespace Adamantium.UI.Controls.DataGrid;

/// <summary>Where a <see cref="DataGridSearchPanel"/>'s search stands - what the theme says beside the field.</summary>
public enum DataGridSearchState
{
    /// <summary>Nothing typed: a search that was never made says nothing.</summary>
    Idle,

    /// <summary>The table is still being read and nothing has been found yet.</summary>
    Searching,

    /// <summary>The whole table was read and nothing holds what was typed.</summary>
    NoMatches,

    /// <summary>Found, and the count is final.</summary>
    Matches,

    /// <summary>Found, and the table is still being read: the count can still grow.</summary>
    MatchesSoFar
}
