using System;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.Core.Resources;

/// <summary>The OS light/dark appearance for the session, set by the platform and read when resolving
/// <see cref="ThemeVariant.System"/>. <see cref="Changed"/> fires only on an actual change.</summary>
public static class SystemAppearance
{
    private static bool _prefersDark;
    private static string _fontFamily;
    private static string _monospaceFontFamily;
    private static Task _reading;

    /// <summary>Whether the OS currently asks for a dark appearance. Written by the platform layer.</summary>
    public static bool PrefersDark
    {
        get
        {
            AwaitReading();
            return _prefersDark;
        }
        set
        {
            AwaitReading();
            if (_prefersDark == value) return;   // only a real change is worth telling anyone about
            _prefersDark = value;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>The family of the desktop's interface font, where the platform says; null elsewhere.</summary>
    public static string FontFamily
    {
        get
        {
            AwaitReading();
            return _fontFamily;
        }
        set
        {
            AwaitReading();
            _fontFamily = value;
        }
    }

    /// <summary>The family of the desktop's monospace font, for code and terminals, where the platform says; null
    /// elsewhere.</summary>
    public static string MonospaceFontFamily
    {
        get
        {
            AwaitReading();
            return _monospaceFontFamily;
        }
        set
        {
            AwaitReading();
            _monospaceFontFamily = value;
        }
    }

    /// <summary>Raised when the OS appearance actually changes - day turning to night, or the user flipping the
    /// setting. Handlers must not throw.</summary>
    public static event EventHandler Changed;

    /// <summary>Takes the desktop's settings from <paramref name="read"/>, run on the thread pool alongside the rest of
    /// startup: <see cref="PrefersDark"/>, <see cref="FontFamily"/> and <see cref="MonospaceFontFamily"/> wait for it
    /// when first asked. What it reads is where they start from, so it raises no <see cref="Changed"/>.</summary>
    internal static void ReadInBackground(Func<(bool? PrefersDark, string FontFamily, string MonospaceFontFamily)> read)
    {
        Volatile.Write(ref _reading, Task.Run(() =>
        {
            (bool? PrefersDark, string FontFamily, string MonospaceFontFamily) settings;
            try
            {
                settings = read();
            }
            catch (Exception e)
            {
                Serilog.Log.Logger.Error(e, "Reading the desktop's settings failed; the defaults stand");
                return;
            }

            var (prefersDark, fontFamily, monospaceFontFamily) = settings;
            if (prefersDark is { } dark)
            {
                _prefersDark = dark;
            }

            if (fontFamily != null)
            {
                _fontFamily = fontFamily;
            }

            if (monospaceFontFamily != null)
            {
                _monospaceFontFamily = monospaceFontFamily;
            }
        }));
    }

    private static void AwaitReading()
    {
        if (Volatile.Read(ref _reading) is { } reading)
        {
            reading.GetAwaiter().GetResult();
            Interlocked.CompareExchange(ref _reading, null, reading);
        }
    }
}
