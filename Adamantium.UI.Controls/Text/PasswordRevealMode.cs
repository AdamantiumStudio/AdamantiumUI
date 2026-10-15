namespace Adamantium.UI.Controls.Text;

/// <summary>How a <see cref="PasswordBox"/>'s reveal button shows the password.</summary>
public enum PasswordRevealMode
{
    /// <summary>Shown while the button is held down.</summary>
    Peek,

    /// <summary>A click shows it, the next one masks it again.</summary>
    Toggle,

    /// <summary>No button: the password stays masked unless <see cref="PasswordBox.IsPasswordRevealed"/> is set.</summary>
    Hidden
}
