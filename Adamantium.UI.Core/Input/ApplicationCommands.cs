namespace Adamantium.UI.Core.Input;

/// <summary>The commands every application has - the clipboard, undo, selecting all, finding - with their usual keys. The
/// element that has the focus does them: a text box copies its selection, an editor its own.</summary>
public static class ApplicationCommands
{
    public static RoutedCommand Cut { get; } = new(nameof(Cut), typeof(ApplicationCommands),
        new KeyGesture(Key.X, ModifierKeys.Control), new KeyGesture(Key.Delete, ModifierKeys.Shift));

    public static RoutedCommand Copy { get; } = new(nameof(Copy), typeof(ApplicationCommands),
        new KeyGesture(Key.C, ModifierKeys.Control), new KeyGesture(Key.Insert, ModifierKeys.Control));

    public static RoutedCommand Paste { get; } = new(nameof(Paste), typeof(ApplicationCommands),
        new KeyGesture(Key.V, ModifierKeys.Control), new KeyGesture(Key.Insert, ModifierKeys.Shift));

    public static RoutedCommand Undo { get; } = new(nameof(Undo), typeof(ApplicationCommands),
        new KeyGesture(Key.Z, ModifierKeys.Control));

    public static RoutedCommand Redo { get; } = new(nameof(Redo), typeof(ApplicationCommands),
        new KeyGesture(Key.Y, ModifierKeys.Control), new KeyGesture(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));

    public static RoutedCommand SelectAll { get; } = new(nameof(SelectAll), typeof(ApplicationCommands),
        new KeyGesture(Key.A, ModifierKeys.Control));

    public static RoutedCommand Find { get; } = new(nameof(Find), typeof(ApplicationCommands),
        new KeyGesture(Key.F, ModifierKeys.Control));

    public static RoutedCommand Replace { get; } = new(nameof(Replace), typeof(ApplicationCommands),
        new KeyGesture(Key.H, ModifierKeys.Control));
}
