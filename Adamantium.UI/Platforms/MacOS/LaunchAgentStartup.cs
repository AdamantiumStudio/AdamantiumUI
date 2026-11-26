using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The user's launch agents: a property list named after the application has the system start it at sign-in. The
/// system's own settings turn one off without taking it out, which an application is not told.</summary>
internal sealed class LaunchAgentStartup : ISystemStartup
{
    private const string AgentsFolder = "Library/LaunchAgents";
    private const string ArgumentsKey = "ProgramArguments";
    private const string DotnetHost = "dotnet";

    private readonly string _path;
    private readonly string _label;
    private readonly string[] _arguments;

    public LaunchAgentStartup() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AgentsFolder),
        "adamantium." + (Assembly.GetEntryAssembly()?.GetName().Name ?? "Application"),
        ArgumentsOf(Assembly.GetEntryAssembly()))
    {
    }

    /// <summary>The agent <paramref name="label"/> in <paramref name="folder"/>, running <paramref name="arguments"/>.
    /// </summary>
    internal LaunchAgentStartup(string folder, string label, string[] arguments)
    {
        _path = Path.Combine(folder, label + ".plist");
        _label = label;
        _arguments = arguments;
    }

    public bool IsOn
    {
        get => IsListed();
        set
        {
            if (value)
                PutIn();
            else if (File.Exists(_path))
                File.Delete(_path);
        }
    }

    private bool IsListed()
    {
        if (!File.Exists(_path))
            return false;

        try
        {
            var arguments = XDocument.Load(_path).Root?.Element("dict")?.Elements("key")
                .FirstOrDefault(key => key.Value == ArgumentsKey)?.ElementsAfterSelf().FirstOrDefault();
            return arguments != null && arguments.Elements("string").Select(element => element.Value).SequenceEqual(_arguments);
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void PutIn()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        new XDocument(
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
                new XElement("key", "Label"), new XElement("string", _label),
                new XElement("key", ArgumentsKey),
                new XElement("array", _arguments.Select(argument => new XElement("string", argument))),
                new XElement("key", "RunAtLoad"), new XElement("true"),
                new XElement("key", "ProcessType"), new XElement("string", "Interactive")))).Save(_path);
    }

    private static string[] ArgumentsOf(Assembly assembly)
    {
        var process = Environment.ProcessPath ?? assembly?.Location ?? string.Empty;
        return string.Equals(Path.GetFileNameWithoutExtension(process), DotnetHost, StringComparison.OrdinalIgnoreCase)
            ? [process, assembly?.Location ?? string.Empty, ISystemStartup.Argument]
            : [process, ISystemStartup.Argument];
    }
}
