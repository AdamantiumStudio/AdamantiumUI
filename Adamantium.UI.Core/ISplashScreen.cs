using System;
using System.Threading.Tasks;

namespace Adamantium.UI.Core;

/// <summary>The splash window shown while the application runs (<see cref="IUIApplication.ShowSplash(string)"/>): it
/// goes when this is disposed.</summary>
public interface ISplashScreen : IDisposable
{
    /// <summary>Completes once the window is on screen - or could not come on it, or has gone already: a window it
    /// stands in for can go then without leaving nothing in its place.</summary>
    Task Shown { get; }
}
