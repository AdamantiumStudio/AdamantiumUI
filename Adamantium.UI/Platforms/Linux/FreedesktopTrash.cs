using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The trash of the freedesktop.org Trash specification, which GNOME, KDE and the rest share. What lies on the
/// home folder's file system goes to the home trash, <c>$XDG_DATA_HOME/Trash</c>; what lies on another, to that file
/// system's own: <c>$topdir/.Trash/$uid</c> where the administrator made <c>.Trash</c> as the specification asks, else
/// <c>$topdir/.Trash-$uid</c>. Each item is moved into the trash's <c>files</c> under a name of its own, its
/// <c>.trashinfo</c> in <c>info</c> written first - which reserves the name - saying where it was and when it went. A
/// volume's trash folder is taken only as a folder of the user's alone, never through a link someone could have left
/// there; a folder that is a volume of its own is not trashed.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class FreedesktopTrash : ITrashPlatform
{
    private const string MountInfo = "/proc/self/mountinfo";
    private const string ProcessStatus = "/proc/self/status";
    private const UnixFileMode StickyBit = UnixFileMode.StickyBit;
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public bool MoveToTrash(string path)
    {
        try
        {
            var trash = FindTrash(RealPath(path), out var originalPath);
            if (trash == null)
            {
                return false;
            }

            var files = Path.Combine(trash, "files");
            var info = Path.Combine(trash, "info");
            Directory.CreateDirectory(files, OwnerOnly);
            Directory.CreateDirectory(info, OwnerOnly);

            var name = ReserveName(files, info, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), originalPath,
                out var infoFile);
            try
            {
                var target = Path.Combine(files, name);
                if (Directory.Exists(path))
                {
                    Directory.Move(path, target);
                }
                else
                {
                    File.Move(path, target);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                File.Delete(infoFile);
                return false;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string FindTrash(string path, out string originalPath)
    {
        originalPath = path;
        var home = HomeTrash();
        var mounts = ReadMountPoints();
        var mount = FindMountPoint(mounts, path);
        if (string.Equals(mount, path, StringComparison.Ordinal))
        {
            return null;
        }

        Directory.CreateDirectory(home, OwnerOnly);
        if (string.Equals(mount, FindMountPoint(mounts, RealPath(home)), StringComparison.Ordinal))
        {
            return home;
        }

        originalPath = Path.GetRelativePath(mount, path);
        var uid = ReadUserId();
        if (uid == null)
        {
            return null;
        }

        var shared = Path.Combine(mount, ".Trash");
        if (IsSharedTrash(shared) && OwnFolder(Path.Combine(shared, uid)) is { } inShared)
        {
            return inShared;
        }

        return OwnFolder(Path.Combine(mount, ".Trash-" + uid));
    }

    private static string OwnFolder(string folder)
    {
        try
        {
            var made = Directory.CreateDirectory(folder, OwnerOnly);
            var mode = made.UnixFileMode & (UnixFileMode)0x1FF;
            return made.LinkTarget == null && mode == OwnerOnly ? made.FullName : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string HomeTrash()
    {
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(data) || !Path.IsPathRooted(data))
        {
            data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(data, "Trash");
    }

    private static bool IsSharedTrash(string folder)
    {
        var shared = new DirectoryInfo(folder);
        return shared.Exists && shared.LinkTarget == null && shared.UnixFileMode.HasFlag(StickyBit);
    }

    private static string ReserveName(string files, string info, string name, string originalPath, out string infoFile)
    {
        var content = "[Trash Info]\n" +
                      $"Path={EscapePath(originalPath)}\n" +
                      $"DeletionDate={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}\n";
        for (var attempt = 1; ; attempt++)
        {
            var candidate = attempt == 1 ? name : $"{Path.GetFileNameWithoutExtension(name)}.{attempt}{Path.GetExtension(name)}";
            infoFile = Path.Combine(info, candidate + ".trashinfo");
            if (File.Exists(Path.Combine(files, candidate)) || Directory.Exists(Path.Combine(files, candidate)))
            {
                continue;
            }

            FileStream stream;
            try
            {
                stream = new FileStream(infoFile, FileMode.CreateNew, FileAccess.Write);
            }
            catch (IOException) when (File.Exists(infoFile))
            {
                continue;
            }

            try
            {
                using (stream)
                {
                    stream.Write(Encoding.UTF8.GetBytes(content));
                }
            }
            catch (IOException)
            {
                File.Delete(infoFile);
                throw;
            }

            return candidate;
        }
    }

    private static string RealPath(string path)
    {
        var real = "/";
        foreach (var part in Path.GetDirectoryName(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(real, part);
            real = new DirectoryInfo(next).ResolveLinkTarget(true)?.FullName ?? next;
        }

        return Path.Combine(real, Path.GetFileName(path));
    }

    private static string EscapePath(string path)
    {
        var segments = path.Split('/').Select(Uri.EscapeDataString);
        return string.Join('/', segments);
    }

    private static string[] ReadMountPoints()
    {
        try
        {
            return File.ReadLines(MountInfo)
                .Select(line => line.Split(' '))
                .Where(fields => fields.Length > 4)
                .Select(fields => UnescapeMountPoint(fields[4]))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ["/"];
        }
    }

    private static string FindMountPoint(string[] mounts, string path) =>
        mounts.Where(mount => mount == "/" || path == mount || path.StartsWith(mount + "/", StringComparison.Ordinal))
            .MaxBy(mount => mount.Length) ?? "/";

    private static string UnescapeMountPoint(string field)
    {
        var text = new StringBuilder(field.Length);
        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 3 < field.Length
                && int.TryParse(field.AsSpan(i + 1, 3), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                text.Append((char)Convert.ToInt32(field.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                text.Append(field[i]);
            }
        }

        return text.ToString();
    }

    private static string ReadUserId()
    {
        try
        {
            var line = File.ReadLines(ProcessStatus).FirstOrDefault(row => row.StartsWith("Uid:", StringComparison.Ordinal));
            return line?.Split((char[])['\t', ' '], StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
