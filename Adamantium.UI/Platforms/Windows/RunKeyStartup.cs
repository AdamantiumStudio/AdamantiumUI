using System;
using System.IO;
using System.Reflection;
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

    public bool IsOn
    {
        get => IsListed() && !IsTurnedOff();
        set
        {
            ForgetTurnedOff();
            if (value)
                PutIn();
            else
                TakeOut();
        }
    }

    // A path names the same program in any case.
    private bool IsListed()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_runKey);
        return string.Equals(run?.GetValue(_name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string,
            _command, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTurnedOff()
    {
        using var approved = Registry.CurrentUser.OpenSubKey(_approvedKey);
        return approved?.GetValue(_name) is byte[] { Length: > 0 } mark && (mark[0] & 1) == 1;
    }

    private void ForgetTurnedOff()
    {
        using var approved = Registry.CurrentUser.OpenSubKey(_approvedKey, true);
        approved?.DeleteValue(_name, false);
    }

    private void PutIn()
    {
        using var run = Registry.CurrentUser.CreateSubKey(_runKey);
        run.SetValue(_name, _command, RegistryValueKind.String);
    }

    private void TakeOut()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_runKey, true);
        run?.DeleteValue(_name, false);
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
