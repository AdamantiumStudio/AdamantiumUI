using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Decorators;

namespace Adamantium.XamlTests;

/// <summary>An element with an event and two handlers of its own, so markup can subscribe one and an edit swap it.</summary>
public class EventProbe : Border
{
    public event EventHandler Fired;

    public List<string> Heard { get; } = [];

    public void Fire()
    {
        Fired?.Invoke(this, EventArgs.Empty);
    }

    public void OnA(object sender, EventArgs e)
    {
        Heard.Add("A");
    }

    public void OnB(object sender, EventArgs e)
    {
        Heard.Add("B");
    }
}
