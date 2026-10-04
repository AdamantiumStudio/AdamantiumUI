using System;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.UI.Automation.Cli;

internal sealed class Arguments
{
    private static readonly string[] Flags = ["allow-errors"];

    private readonly List<string> _positional = [];
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public Arguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i].StartsWith("--") ? args[i][2..] : null;
            if (name != null && Flags.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _flags.Add(name);
            }
            else if (name != null && i + 1 < args.Length)
            {
                _options[name] = args[++i];
            }
            else
            {
                _positional.Add(args[i]);
            }
        }
    }

    public string Command => _positional.Count > 0 ? _positional[0].ToLowerInvariant() : null;

    public string At(int index) => index + 1 < _positional.Count ? _positional[index + 1] : null;

    public IEnumerable<string> From(int index) => _positional.Skip(index + 1);

    public string Option(string name) => _options.GetValueOrDefault(name);

    public bool Flag(string name) => _flags.Contains(name);

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
