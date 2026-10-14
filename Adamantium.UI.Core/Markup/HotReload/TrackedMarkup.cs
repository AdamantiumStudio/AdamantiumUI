using System.Runtime.CompilerServices;
using Adamantium.UI.Markup;
using Adamantium.UI.Markup.Building;

namespace Adamantium.UI.Core.Markup;

internal sealed class TrackedMarkup(string path, string builtText, string builtHash)
{
    private readonly object _sync = new();
    private readonly List<WeakReference<object>> _instances = [];
    private readonly ConditionalWeakTable<object, BuildPlan> _planOf = new();
    private LoweredDocument _built;
    private LoweredDocument _current;
    private string _currentHash;

    public string Path { get; } = path;

    public SemaphoreSlim Gate { get; } = new(1, 1);

    public bool KnowsWhatWasBuilt => builtText != null;

    public void Track(object instance, string hash, List<string> diagnostics)
    {
        lock (_sync)
        {
            _instances.RemoveAll(reference => !reference.TryGetTarget(out _));
            if (_current == null || hash == builtHash)
            {
                _instances.Add(new WeakReference<object>(instance));
                if (_current != null)
                {
                    ReconcileGuarded(instance, _current, diagnostics);
                }

                return;
            }

            if (hash == _currentHash)
            {
                _instances.Add(new WeakReference<object>(instance));
                _planOf.AddOrUpdate(instance, _current.Plan);
                return;
            }

            diagnostics.Add($"{System.IO.Path.GetFileName(Path)}: an instance was built from a version this run has not seen; it is not followed");
        }
    }

    public bool EnsureBuiltPlan()
    {
        lock (_sync)
        {
            _built ??= AumlLoader.Lower(builtText, null, [], Path);
            return _built?.Plan != null;
        }
    }

    public int Apply(LoweredDocument edited, string hash, List<string> diagnostics)
    {
        lock (_sync)
        {
            var applied = 0;
            foreach (var reference in _instances)
            {
                if (reference.TryGetTarget(out var instance) && ReconcileGuarded(instance, edited, diagnostics))
                {
                    applied++;
                }
            }

            _instances.RemoveAll(reference => !reference.TryGetTarget(out _));
            _current = edited;
            _currentHash = hash;
            return applied;
        }
    }

    private bool ReconcileGuarded(object instance, LoweredDocument edited, List<string> diagnostics)
    {
        try
        {
            return edited.EntityType switch
            {
                EntityType.StyleSet or EntityType.ResourceDictionary or EntityType.ThemeVariant => Rebuild(instance, edited, diagnostics),
                EntityType.Theme => Refuse(diagnostics, "a theme's own file changed - restart the application to see it"),
                _ => Reconcile(instance, edited, diagnostics),
            };
        }
        catch (Exception exception)
        {
            diagnostics.Add($"{instance.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool Rebuild(object instance, LoweredDocument edited, List<string> diagnostics)
    {
        if (instance is not IMarkupContent content)
        {
            return Refuse(diagnostics, $"{instance.GetType().Name} cannot be built again in place");
        }

        content.ClearMarkupContent();
        new BuildPlanExecutor(edited.Resolver, edited.Assemblies, null, diagnostics).Run(edited.Plan, instance);
        _planOf.AddOrUpdate(instance, edited.Plan);
        return true;
    }

    private bool Refuse(List<string> diagnostics, string why)
    {
        diagnostics.Add($"{System.IO.Path.GetFileName(Path)}: {why}");
        return false;
    }

    private bool Reconcile(object instance, LoweredDocument edited, List<string> diagnostics)
    {
        var from = _planOf.TryGetValue(instance, out var plan) ? plan : _built.Plan;
        var oldRoot = from.Steps.OfType<ElementStep>().FirstOrDefault();
        var newRoot = edited.Plan.Steps.OfType<ElementStep>().FirstOrDefault();
        if (oldRoot == null || newRoot == null
            || !string.Equals(oldRoot.Node.TypeReference?.Name, newRoot.Node.TypeReference?.Name, StringComparison.Ordinal))
        {
            diagnostics.Add($"{System.IO.Path.GetFileName(Path)}: the root element changed - restart the application to see it");
            return false;
        }

        var executor = new BuildPlanExecutor(edited.Resolver, edited.Assemblies, null, diagnostics);
        new BuildPlanReconciler(executor, edited.Resolver, diagnostics, instance).Reconcile(instance, oldRoot, newRoot);
        _planOf.AddOrUpdate(instance, edited.Plan);
        return true;
    }
}
