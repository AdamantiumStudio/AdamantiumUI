using System;
using System.Collections.Generic;

namespace Adamantium.UI.Automation.Cli;

internal sealed class Arguments
{
    private readonly List<string> _positional = [];
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

    public Arguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--") && i + 1 < args.Length)
            {
                _options[args[i][2..]] = args[++i];
            }
            else
            {
                _positional.Add(args[i]);
            }
        }
    }

    public string Command => _positional.Count > 0 ? _positional[0].ToLowerInvariant() : null;

    public string At(int index) => index + 1 < _positional.Count ? _positional[index + 1] : null;

    public string Option(string name) => _options.GetValueOrDefault(name);

    public int IntOption(string name) => int.TryParse(Option(name), out var value) ? value : 0;

    public TimeSpan? TimeOption(string name)
    {
        var text = Option(name);
        if (text == null)
        {
            return null;
        }

        if (text.EndsWith("ms") && double.TryParse(text[..^2], out var ms))
        {
            return TimeSpan.FromMilliseconds(ms);
        }

        if (text.EndsWith('s') && double.TryParse(text[..^1], out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return double.TryParse(text, out var plain) ? TimeSpan.FromMilliseconds(plain) : null;
    }
}
