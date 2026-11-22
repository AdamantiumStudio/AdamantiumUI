using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using Adamantium.UI.Core;
using Microsoft.Win32;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>The user's Run key: a value named after the application holds the command that starts it. Task Manager
/// turns a value off without taking it out, in StartupApproved - an odd first byte; putting the application back in the
/// list takes that mark away.</summary>
internal sealed class RunKeyStartup : ISystemStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string DotnetHost = "dotnet";

    private readonly string _runKey;
    private readonly string _approvedKey;
    private readonly string _name;
    private readonly string _command;

    public RunKeyStartup() : this(RunKey, ApprovedKey, ApplicationName(), CommandOf(Assembly.GetEntryAssembly()))
    {
    }

    /// <summary>The value <paramref name="name"/> under <paramref name="runKey"/> of the current user, turned off under
    /// <paramref name="approvedKey"/>, starting <paramref name="command"/>.</summary>
    internal RunKeyStartup(string runKey, string approvedKey, string name, string command)
    {
        _runKey = runKey;
        _approvedKey = approvedKey;
        _name = name;
        _command = command;
    }

    [SupportedOSPlatform("windows")]
    public bool IsOn
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(_runKey);
            // A path names the same program in any case.
            if (!string.Equals(run?.GetValue(_name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string,
                    _command, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var approved = Registry.CurrentUser.OpenSubKey(_approvedKey);
            return approved?.GetValue(_name) is not byte[] { Length: > 0 } mark || (mark[0] & 1) == 0;
        }
        set
        {
            using (var approved = Registry.CurrentUser.OpenSubKey(_approvedKey, true))
            {
                approved?.DeleteValue(_name, false);
            }

            if (value)
            {
                using var run = Registry.CurrentUser.CreateSubKey(_runKey);
                run.SetValue(_name, _command, RegistryValueKind.String);
            }
            else
            {
                using var run = Registry.CurrentUser.OpenSubKey(_runKey, true);
                run?.DeleteValue(_name, false);
            }
        }
    }

    /// <summary>The command starting <paramref name="assembly"/> with <see cref="ISystemStartup.Argument"/>: the program
    /// and, under the dotnet host, the assembly, each quoted - a Windows path holds no quote.</summary>
    internal static string CommandOf(Assembly assembly)
    {
        var process = Environment.ProcessPath ?? assembly.Location;
        var program = string.Equals(Path.GetFileNameWithoutExtension(process), DotnetHost, StringComparison.OrdinalIgnoreCase)
            ? $"\"{process}\" \"{assembly.Location}\""
            : $"\"{process}\"";
        return program + " " + ISystemStartup.Argument;
    }

    private static string ApplicationName() => Assembly.GetEntryAssembly()?.GetName().Name ?? "Adamantium";
}
