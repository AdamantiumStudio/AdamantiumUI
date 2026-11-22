using System;
using Adamantium.Core.DependencyInjection;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.UI.Core;
using Adamantium.UI.Platforms.Linux;
using Adamantium.UI.Platforms.MacOS;
using Adamantium.UI.Platforms.Windows;

namespace Adamantium.UI;

public static class ApplicationBuilder
{
    public static void Build(IDependencyContainer container)
    {
        container.RegisterSingleton<IGraphicsDeviceFactory, GraphicsDeviceFactory>();
        if (OperatingSystem.IsWindows())
        {
            WindowsPlatform.Initialize(container);
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacOSPlatform.Initialize(container);
        }
        else if (OperatingSystem.IsLinux())
        {
            LinuxPlatform.Initialize(container);
        }
    }
}