using System;
using System.IO;
using Adamantium.Graphics.Core;

namespace Adamantium.UI.Sandbox;

/// <summary>The descriptor model the sandbox starts with, kept in its data folder. The device takes it when it is made,
/// so a choice applies from the next start.</summary>
public static class DescriptorModelChoice
{
    private static string FilePath => Path.Combine(ApplicationData.Folder, "descriptor-model.txt");

    public static DescriptorModel Read()
    {
        try
        {
            return File.Exists(FilePath) && Enum.TryParse<DescriptorModel>(File.ReadAllText(FilePath).Trim(), out var model)
                ? model
                : DescriptorModel.Auto;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return DescriptorModel.Auto;
        }
    }

    public static void Write(DescriptorModel model)
    {
        Directory.CreateDirectory(ApplicationData.Folder);
        File.WriteAllText(FilePath, model.ToString());
    }
}
