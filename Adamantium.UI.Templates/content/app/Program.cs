namespace AdamantiumApp;

public static class Program
{
    // Drag and drop to and from other applications needs a single-threaded apartment.
    [STAThread]
    public static void Main()
    {
        var app = new App { StartupType = typeof(MainWindow) };
        app.Run();
    }
}
