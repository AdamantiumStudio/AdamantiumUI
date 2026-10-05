using System;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;

namespace Adamantium.UI.Automation;

public static class AutomationAgentExtensions
{
    /// <summary>Opts the application into being driven from outside. When <see cref="AutomationProtocol.PipeVariable"/>
    /// names a pipe, an <see cref="AutomationAgent"/> listens on it once the application has started, and windows open in
    /// the background - no focus taken, no taskbar button, no remembered placement - so a driven instance stays out of
    /// the way of the person at the computer. Without the variable it does nothing. Call it before
    /// <see cref="UIApplication.Run()"/>.</summary>
    public static UIApplication UseAutomationAgent(this UIApplication application)
    {
        var pipe = Environment.GetEnvironmentVariable(AutomationProtocol.PipeVariable);
        if (string.IsNullOrEmpty(pipe))
        {
            return application;
        }

        WindowBase.ActivateOnShowProperty.OverrideMetadata(typeof(Window), new PropertyMetadata(false));
        ErrorJournal.Install();

        var agent = new AutomationAgent(application, pipe);
        application.Started += (_, _) => agent.Start();
        application.ShuttingDown += (_, _) => agent.Dispose();
        return application;
    }
}
