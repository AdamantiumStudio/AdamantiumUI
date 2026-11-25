using System;
using NUnit.Framework;

/// <summary>The scenarios start the sandbox on the platform's own theme: MacOs on macOS, Fluent elsewhere. ADAM_THEME set
/// outside still wins.</summary>
[SetUpFixture]
public class PinnedThemeSetup
{
    private const string ThemeVariable = "ADAM_THEME";

    [OneTimeSetUp]
    public void PinTheme()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ThemeVariable)))
            Environment.SetEnvironmentVariable(ThemeVariable, OperatingSystem.IsMacOS() ? "macOS" : "Fluent");
    }
}
