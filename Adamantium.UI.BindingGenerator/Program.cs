using Adamantium.UI.BindingGenerator.Cef;
using QuantumBinding.Generator;

namespace Adamantium.UI.BindingGenerator;

/// <summary>Generates the UI's bindings: those named on the command line (cef), or all of them.</summary>
public static class Program
{
    private static readonly Dictionary<string, Func<QuantumBindingGenerator>> Generators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cef"] = () => new CefBindingGenerator(),
        };

    public static int Main(string[] args)
    {
        var names = args.Length > 0 ? args.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : Generators.Keys.ToArray();
        var unknown = names.Where(n => !Generators.ContainsKey(n)).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine($"Unknown binding: {string.Join(", ", unknown)}. Known: {string.Join(", ", Generators.Keys)}.");
            return 1;
        }

        foreach (var name in names)
        {
            Generators[name]().Run();
            Console.WriteLine($"{name}: generated");
        }

        return 0;
    }
}
