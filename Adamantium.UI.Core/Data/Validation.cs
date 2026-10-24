using System.Collections.Generic;

namespace Adamantium.UI.Core.Data;

/// <summary>What is wrong with the values an element shows, as its source says: a binding whose source implements
/// <see cref="System.ComponentModel.INotifyDataErrorInfo"/> carries the errors of the bound property onto its target
/// element, as <see cref="HasErrorProperty"/>, <see cref="ErrorsProperty"/> and <see cref="ErrorProperty"/>. A theme
/// draws a field in error from <c>Validation.HasError</c>; the element's errors are those of all its bindings.</summary>
public static class Validation
{
    /// <summary>Whether a binding of the element has errors.</summary>
    public static readonly AdamantiumProperty HasErrorProperty = AdamantiumProperty.RegisterAttachedReadOnly("HasError",
        typeof(bool), typeof(AdamantiumComponent), new PropertyMetadata(false));

    /// <summary>The errors of the element's bindings, in the order the bindings were bound; empty when none.</summary>
    public static readonly AdamantiumProperty ErrorsProperty = AdamantiumProperty.RegisterAttachedReadOnly("Errors",
        typeof(IReadOnlyList<string>), typeof(AdamantiumComponent), new PropertyMetadata(System.Array.Empty<string>()));

    /// <summary>The first of <see cref="ErrorsProperty"/>; empty when none.</summary>
    public static readonly AdamantiumProperty ErrorProperty = AdamantiumProperty.RegisterAttachedReadOnly("Error",
        typeof(string), typeof(AdamantiumComponent), new PropertyMetadata(string.Empty));

    private static readonly AdamantiumProperty SourcesProperty = AdamantiumProperty.RegisterAttached("ErrorSources",
        typeof(ErrorSources), typeof(AdamantiumComponent), new PropertyMetadata(null));

    public static bool GetHasError(AdamantiumComponent element) => element.GetValue<bool>(HasErrorProperty);

    public static IReadOnlyList<string> GetErrors(AdamantiumComponent element) =>
        element.GetValue<IReadOnlyList<string>>(ErrorsProperty);

    public static string GetError(AdamantiumComponent element) => element.GetValue<string>(ErrorProperty);

    /// <summary>Takes <paramref name="errors"/> as what <paramref name="binding"/> says is wrong on
    /// <paramref name="element"/>; none takes the binding's errors away.</summary>
    internal static void Set(AdamantiumComponent element, BindingExpressionBase binding, IReadOnlyList<string> errors)
    {
        var sources = element.GetValue<ErrorSources>(SourcesProperty);
        if (sources == null)
        {
            if (errors.Count == 0)
            {
                return;
            }

            sources = new ErrorSources();
            element.SetValue(SourcesProperty, sources);
        }

        if (!sources.Set(binding, errors))
        {
            return;
        }

        var all = sources.All();
        element.SetCurrentValue(ErrorsProperty, all);
        element.SetCurrentValue(ErrorProperty, all.Count > 0 ? all[0] : string.Empty);
        element.SetCurrentValue(HasErrorProperty, all.Count > 0);
    }

    private sealed class ErrorSources
    {
        private readonly List<(BindingExpressionBase Binding, IReadOnlyList<string> Errors)> _sources = [];

        public bool Set(BindingExpressionBase binding, IReadOnlyList<string> errors)
        {
            var at = _sources.FindIndex(source => ReferenceEquals(source.Binding, binding));
            if (errors.Count == 0)
            {
                if (at < 0)
                {
                    return false;
                }

                _sources.RemoveAt(at);
                return true;
            }

            if (at < 0)
            {
                _sources.Add((binding, errors));
            }
            else
            {
                _sources[at] = (binding, errors);
            }

            return true;
        }

        public IReadOnlyList<string> All()
        {
            var all = new List<string>();
            foreach (var source in _sources)
            {
                all.AddRange(source.Errors);
            }

            return all;
        }
    }
}
