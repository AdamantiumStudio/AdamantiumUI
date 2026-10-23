using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Adamantium.Imaging;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The application's desktop entry, which GNOME takes a window's icon from: a desktop file named by the Wayland
/// app id, hidden from the application list, and the icon in the user's icon theme. Written only where no other entry
/// stands for the app id - by its name or by its window class - and never over an icon it did not write; written again
/// only when it changed.</summary>
internal static class DesktopEntry
{
    private const string MetadataKey = "Adamantium.DesktopEntry";
    private const string Group = "[Desktop Entry]";
    private const char KeySeparator = '=';
    private const char ListSeparator = ';';
    private const char LineEnd = '\n';
    private const string TypeKey = "Type";
    private const string ApplicationType = "Application";
    private const string NameKey = "Name";
    private const string IconKey = "Icon";
    private const string ExecKey = "Exec";
    private const string NoDisplayKey = "NoDisplay";
    private const string WindowClassKey = "StartupWMClass";
    private const string GeneratedKey = "X-Adamantium-Generated";
    private const string IconsKey = "X-Adamantium-Icons";
    private const string LooseIcon = "file";
    private const string True = "true";
    private const string False = "false";
    private const string DesktopExtension = ".desktop";
    private const string DesktopPattern = "*.desktop";
    private const string IconExtension = ".png";
    private const string TemporaryExtension = ".tmp";
    private const string ApplicationsFolder = "applications";
    private const string IconsFolder = "icons";
    private const string ThemeFolder = "hicolor";
    private const string AppsFolder = "apps";
    private const string SizeFolderFormat = "{0}x{0}";
    private const char IdSeparator = '-';
    private const string DataHomeVariable = "XDG_DATA_HOME";
    private const string DataDirsVariable = "XDG_DATA_DIRS";
    private const string DotnetHost = "dotnet";
    private const string DefaultLocalDataDir = "/usr/local/share";
    private const string DefaultDataDir = "/usr/share";
    private const string LocalFolder = ".local";
    private const string ShareFolder = "share";
    private const string Shell = "/bin/sh";
    private const string ShellCommand = "-c";
    private const string ShellRunsItsArguments = "exec \"$0\" \"$@\"";
    private const string FieldCode = "%";
    private const string EscapedFieldCode = "%%";

    private const char ArgumentQuote = '"';
    private const char Backslash = '\\';
    private const string QuotedSpecials = "\"`$\\";

    private static readonly int[] ThemeSizes = [16, 22, 24, 32, 48, 64, 96, 128, 256, 512];

    private static readonly (string Character, string Escaped)[] ValueEscapes =
        [("\\", "\\\\"), ("\n", "\\n"), ("\r", "\\r"), ("\t", "\\t")];

    /// <summary>Writes the entry of the running application on the thread pool, unless its build said
    /// AdamantiumDesktopEntry=false.</summary>
    public static Task WriteInBackground(string appId) => Task.Run(() =>
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly();
            if (assembly != null && Wanted(assembly))
            {
                Write(appId, DataHome(), DataDirs(), ApplicationIcon.Load(assembly), NameOf(assembly), ExecOf(assembly));
            }
        }
        catch (Exception e)
        {
            Serilog.Log.Logger.Debug(e, "The application's desktop entry was not written");
        }
    });

    /// <summary>Writes the entry <paramref name="appId"/> into <paramref name="dataHome"/>; false where another entry
    /// stands for that app id in it or in <paramref name="dataDirs"/>, or there is no icon.</summary>
    public static bool Write(string appId, string dataHome, IReadOnlyList<string> dataDirs, ApplicationIcon icon, string name,
        string exec)
    {
        var entryPath = Path.Combine(dataHome, ApplicationsFolder, appId + DesktopExtension);
        if (icon == null || Foreign(appId, dataHome) || Exists(dataDirs, dir => Foreign(appId, dir)))
        {
            return false;
        }

        var written = WrittenIcons(entryPath);
        List<string> icons = [];
        string iconName = null;
        foreach (var picture in icon.Pictures)
        {
            if (Array.IndexOf(ThemeSizes, picture.Size) < 0)
            {
                continue;
            }

            var size = picture.Size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (WriteIcon(ThemeIcon(dataHome, appId, picture.Size), Png(picture), written.Contains(size)))
            {
                icons.Add(size);
                iconName = appId;
            }
        }

        if (iconName == null)
        {
            var loose = Path.Combine(dataHome, IconsFolder, appId + IconExtension);
            if (!WriteIcon(loose, Png(icon.Pictures[0]), written.Contains(LooseIcon)))
            {
                return false;
            }

            icons.Add(LooseIcon);
            iconName = loose;
        }

        foreach (var old in written)
        {
            if (!icons.Contains(old))
            {
                File.Delete(old == LooseIcon
                    ? Path.Combine(dataHome, IconsFolder, appId + IconExtension)
                    : ThemeIcon(dataHome, appId, int.Parse(old, System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        var text = new StringBuilder()
            .Append(Group).Append(LineEnd)
            .Append(TypeKey).Append(KeySeparator).Append(ApplicationType).Append(LineEnd)
            .Append(NameKey).Append(KeySeparator).Append(Escape(name)).Append(LineEnd)
            .Append(IconKey).Append(KeySeparator).Append(Escape(iconName)).Append(LineEnd)
            .Append(ExecKey).Append(KeySeparator).Append(exec).Append(LineEnd)
            .Append(NoDisplayKey).Append(KeySeparator).Append(True).Append(LineEnd)
            .Append(WindowClassKey).Append(KeySeparator).Append(Escape(appId)).Append(LineEnd)
            .Append(GeneratedKey).Append(KeySeparator).Append(True).Append(LineEnd)
            .Append(IconsKey).Append(KeySeparator).Append(string.Join(ListSeparator, icons)).Append(LineEnd)
            .ToString();
        WriteIfChanged(entryPath, Encoding.UTF8.GetBytes(text));
        return true;
    }

    /// <summary>The Exec value that starts <paramref name="program"/> with <paramref name="arguments"/>: each quoted and
    /// escaped as the desktop entry specification asks. A program whose path holds a % is started through the shell:
    /// GLib looks for the program before it turns %% back into %.</summary>
    public static string Exec(string program, params string[] arguments)
    {
        if (program.Contains(FieldCode))
        {
            return Exec(Shell, [ShellCommand, ShellRunsItsArguments, program, .. arguments]);
        }

        var exec = new StringBuilder(Quote(program));
        foreach (var argument in arguments)
        {
            exec.Append(' ').Append(Quote(argument));
        }

        return exec.ToString();
    }

    private static string Quote(string argument)
    {
        var quoted = new StringBuilder().Append(ArgumentQuote);
        foreach (var character in argument)
        {
            if (QuotedSpecials.Contains(character))
            {
                quoted.Append(Backslash);
            }

            quoted.Append(character);
        }

        return Escape(quoted.Append(ArgumentQuote).ToString()).Replace(FieldCode, EscapedFieldCode);
    }

    private static string Escape(string value)
    {
        foreach (var (character, escaped) in ValueEscapes)
        {
            value = value.Replace(character, escaped);
        }

        return value;
    }

    private static string ThemeIcon(string dataHome, string appId, int size) => Path.Combine(dataHome, IconsFolder, ThemeFolder,
        string.Format(System.Globalization.CultureInfo.InvariantCulture, SizeFolderFormat, size), AppsFolder, appId + IconExtension);

    private static bool Exists(IReadOnlyList<string> dirs, Func<string, bool> test)
    {
        foreach (var dir in dirs)
        {
            if (test(dir))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Foreign(string appId, string dataDir)
    {
        var applications = Path.Combine(dataDir, ApplicationsFolder);
        if (!Directory.Exists(applications))
        {
            return false;
        }

        foreach (var file in Directory.EnumerateFiles(applications, DesktopPattern, SearchOption.AllDirectories))
        {
            var id = Path.GetRelativePath(applications, file).Replace(Path.DirectorySeparatorChar, IdSeparator);
            var named = string.Equals(id, appId + DesktopExtension, StringComparison.Ordinal);
            var values = Values(file);
            var classed = values.TryGetValue(WindowClassKey, out var windowClass) &&
                          string.Equals(windowClass, appId, StringComparison.OrdinalIgnoreCase);
            if ((named || classed) && !(values.TryGetValue(GeneratedKey, out var generated) && generated == True))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> WrittenIcons(string entryPath)
    {
        HashSet<string> written = [];
        if (File.Exists(entryPath) && Values(entryPath).TryGetValue(IconsKey, out var list))
        {
            written.UnionWith(list.Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        return written;
    }

    private static Dictionary<string, string> Values(string path)
    {
        var values = new Dictionary<string, string>();
        try
        {
            var inGroup = false;
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    inGroup = trimmed == Group;
                    continue;
                }

                var separator = trimmed.IndexOf(KeySeparator);
                if (inGroup && separator > 0)
                {
                    values.TryAdd(trimmed[..separator].Trim(), trimmed[(separator + 1)..].Trim());
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return values;
    }

    private static bool WriteIcon(string path, byte[] content, bool ours)
    {
        if (File.Exists(path) && !ours)
        {
            return false;
        }

        WriteIfChanged(path, content);
        return true;
    }

    private static void WriteIfChanged(string path, byte[] content)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + TemporaryExtension;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                file.Write(content);
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static byte[] Png(ApplicationIcon.Picture picture)
    {
        using var image = Image.New2D((uint)picture.Size, (uint)picture.Size, SurfaceFormat.R8G8B8A8.UNorm);
        Marshal.Copy(picture.Rgba, 0, image.DataPointer, picture.Rgba.Length);
        using var stream = new MemoryStream();
        image.Save(stream, ImageFileType.Png);
        return stream.ToArray();
    }

    private static bool Wanted(Assembly assembly)
    {
        foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (metadata.Key == MetadataKey && string.Equals(metadata.Value, False, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string DataHome()
    {
        var home = Environment.GetEnvironmentVariable(DataHomeVariable);
        return !string.IsNullOrEmpty(home) && Path.IsPathRooted(home)
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), LocalFolder, ShareFolder);
    }

    private static IReadOnlyList<string> DataDirs()
    {
        var dirs = Environment.GetEnvironmentVariable(DataDirsVariable);
        return string.IsNullOrEmpty(dirs)
            ? [DefaultLocalDataDir, DefaultDataDir]
            : dirs.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string NameOf(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product is { Length: > 0 } product
            ? product
            : assembly.GetName().Name;

    private static string ExecOf(Assembly assembly)
    {
        var process = Environment.ProcessPath ?? assembly.Location;
        return string.Equals(Path.GetFileNameWithoutExtension(process), DotnetHost, StringComparison.Ordinal)
            ? Exec(process, assembly.Location)
            : Exec(process);
    }
}
