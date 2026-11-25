using System;
using NUnit.Framework;

/// <summary>The scenarios start the sandbox on one theme, on every system: the caption they press is that theme's, and
/// the default one is the platform's. ADAM_THEME set outside still wins.</summary>
[SetUpFixture]
public class PinnedThemeSetup
{
    private const string ThemeVariable = "ADAM_THEME";

    [OneTimeSetUp]
    public void PinTheme()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ThemeVariable)))
            Environment.SetEnvironmentVariable(ThemeVariable, "Fluent");
    }
}
