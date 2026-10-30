using System.Threading.Tasks;
using Adamantium.UI.Core;

namespace Adamantium.UI;

/// <summary>A splash window shown while the application runs (<see cref="UIApplication.ShowSplash(string)"/>), up until
/// disposed.</summary>
internal sealed class SplashScreen(SplashHost splash) : ISplashScreen
{
    public Task Shown => splash.Shown;

    public void Dispose() => splash.HandOver();
}
