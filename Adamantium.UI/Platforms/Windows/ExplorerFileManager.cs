using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.UI.Core;
using Adamantium.Win32;
using Adamantium.Win32.Shell;
using Serilog;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>Explorer, asked to select the file in its folder; the shell, to open it; the attachment services, to open or
/// mark a file from outside.</summary>
internal sealed class ExplorerFileManager : IFileManagerPlatform
{
    private const int NoAssociation = 1155;
    private const int NoAssociationResult = unchecked((int)0x80070483);

    // Under it the system keeps what the user chose in its warnings, such as "always ask before opening".
    private static readonly Guid ClientId = new("6A2C1F0E-3B7D-4C59-9E21-8D4F7A0B5C63");

    private const string ShellFolders = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
    private const string DownloadsId = "{374DE290-123F-4565-9164-39C4925E467B}";

    /// <summary>The Downloads folder as the shell records it for the user - moved, if they moved it - under its known
    /// folder id in User Shell Folders, its variables expanded once.</summary>
    public string DownloadsFolder
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            try
            {
                using var folders = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ShellFolders);
                return folders?.GetValue(DownloadsId, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames)
                    is string folder
                    ? Environment.ExpandEnvironmentVariables(folder)
                    : null;
            }
            catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public bool OpenItem(string path)
    {
        try
        {
            using var opened = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == NoAssociation)
        {
            // Nothing opens its kind: the shell asks which application should, as a double click does.
            return OpenWith(path);
        }
        catch (Exception e) when (e is Win32Exception or IOException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool OpenWith(string path)
    {
        try
        {
            using var asking = Process.Start(new ProcessStartInfo("rundll32.exe")
            {
                ArgumentList = { "shell32.dll,OpenAs_RunDLL", path },
                UseShellExecute = false
            });
            return asking != null;
        }
        catch (Exception e) when (e is Win32Exception or IOException or ArgumentException)
        {
            return false;
        }
    }

    public bool IsDangerousToOpen(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Length > 0 && Win32Interop.AssocIsDangerous(extension);
    }

    // The attachment services mark the file (restricted zone), ask the policy and the scanners and show the system's
    // warning themselves. They answer only on a single-threaded apartment, such as the UI thread.
    public bool OpenUntrustedItem(string path, IntPtr ownerWindow) =>
        WithAttachmentServices(path, services =>
        {
            var saved = services.Save();
            if (saved < 0)
            {
                return saved;
            }

            var opened = services.Execute(ownerWindow, null, IntPtr.Zero);
            // Nothing opens its kind: marked already, it goes to the shell's question of which application should.
            return opened == NoAssociationResult && OpenWith(path) ? 0 : opened;
        });

    public bool MarkUntrusted(string path) => WithAttachmentServices(path, services => services.Save());

    private static bool WithAttachmentServices(string path, Func<IAttachmentExecute, int> use)
    {
        if (!OperatingSystem.IsWindows() || AttachmentServices() is not { } services)
        {
            Log.Logger.Warning("The attachment services are not there for {Path}; is the thread [STAThread]?", path);
            return false;
        }

        try
        {
            var client = ClientId;
            return services.SetClientGuid(ref client) >= 0 && services.SetLocalPath(path) >= 0 && use(services) >= 0;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            Marshal.ReleaseComObject(services);
        }
    }

    private static IAttachmentExecute AttachmentServices()
    {
        try
        {
            var clsid = Win32Interop.ClsidAttachmentServices;
            var iid = typeof(IAttachmentExecute).GUID;
            return Win32Interop.CoCreateInstance(ref clsid, IntPtr.Zero, Win32Interop.ClsCtxInprocServer, ref iid,
                out var instance) == 0
                ? instance as IAttachmentExecute
                : null;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            return null;
        }
    }

    public bool ShowItem(string path)
    {
        try
        {
            using var explorer = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\"")
            {
                UseShellExecute = false
            });
            return explorer != null;
        }
        catch (Exception e) when (e is Win32Exception or IOException or ArgumentException)
        {
            return false;
        }
    }
}
