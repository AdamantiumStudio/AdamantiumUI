using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Adamantium.UI.Controls;

namespace Adamantium.UI;

/// <summary>Keeps windows' places in one JSON file in the user's local application data, in a folder named after the
/// application. A damaged or unwritable file forgets the places; it never stops a window opening or closing.</summary>
public sealed class FileWindowPlacementStore : IWindowPlacementStore
{
    private const string FileName = "window-placement.json";

    private readonly string _file;
    private Dictionary<string, WindowPlacement> _placements;

    public FileWindowPlacementStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Assembly.GetEntryAssembly()?.GetName().Name ?? "Adamantium", FileName))
    {
    }

    internal FileWindowPlacementStore(string file)
    {
        _file = file;
    }

    public WindowPlacement Load(string key) => Placements().GetValueOrDefault(key);

    public void Save(string key, WindowPlacement placement)
    {
        Placements()[key] = placement;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file));
            File.WriteAllText(_file, JsonSerializer.Serialize(_placements));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Logger.Warning("Window placement could not be saved to {File}: {Reason}", _file, e.Message);
        }
    }

    private Dictionary<string, WindowPlacement> Placements() => _placements ??= Read() ?? new Dictionary<string, WindowPlacement>();

    private Dictionary<string, WindowPlacement> Read()
    {
        try
        {
            return File.Exists(_file) ? JsonSerializer.Deserialize<Dictionary<string, WindowPlacement>>(File.ReadAllText(_file)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Serilog.Log.Logger.Warning("Window placement could not be read from {File}: {Reason}", _file, e.Message);
            return null;
        }
    }
}
