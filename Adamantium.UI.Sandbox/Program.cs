using System;

namespace Adamantium.UI.Sandbox;

public class Program
{
    // OLE (the OS drag-drop bridge) requires the UI thread to be a single-threaded apartment - the same requirement
    // WPF/WinForms put on their entry point. Without it the app still runs; only drags to/from other applications are off.
    [STAThread]
    public static void Main(string[] args)
    {
        // A dragged picture should also travel as a file: many targets (Paint 3D, packaged apps) ask for a file list
        // and never look at a bitmap. Off by default in the engine because it writes to disk - an application opts in.
        UI.Input.DragDropOptions.OfferImagesAsFiles = true;

        var sandboxApp = new SandboxApplication
        {
            IsFixedTimeStep = false,
            EnableGraphicsDebug = Environment.GetEnvironmentVariable("ADAM_VK_DEBUG") == "1",
            DesiredFPS = 300,
            StartupType = typeof(MainWindow)
        };
        sandboxApp.Run();
    }
}
