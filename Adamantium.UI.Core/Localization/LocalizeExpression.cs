using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Core.Localization;

/// <summary>A live <see cref="Localize"/>: writes its string to the target, or produces it for a parent binding, and
/// writes it again when the application's language changes or a binding that fills a placeholder does.</summary>
public sealed class LocalizeExpression : BindingExpressionBase
{
    private readonly Dictionary<string, BindingExpressionBase> _arguments = new();
    private BindingExpressionBase _key;
    private bool _connecting;
    private FundamentalUIComponent _waitingOn;

    public LocalizeExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty, Localize localize)
    {
        Target = target;
        TargetProperty = targetProperty;
        BindingBase = localize;
        Localize = localize;
    }

    public Localize Localize { get; }

    public override void EstablishConnection()
    {
        CloseConnection();
        Languages.Follow(this);

        // Each argument publishes its value as it connects; the string is written once, when all of them have one.
        _connecting = true;
        _key = Follow(Localize.KeySource);
        foreach (var (name, value) in Localize.Arguments)
        {
            if (Follow(value) is { } argument)
            {
                _arguments[name] = argument;
            }
        }

        _connecting = false;
        UpdateTarget();
    }

    private BindingExpressionBase Follow(object value)
    {
        if (Followed(value) is not { } binding)
        {
            return null;
        }

        var expression = binding.CreateExpression(Target, null);
        expression.ValueChanged += OnArgumentChanged;
        expression.EstablishConnection();
        return expression;
    }

    // What an argument follows: a binding as it is, or a property of the control the template was built for.
    private BindingBase Followed(object value)
    {
        if (value is not TemplateBinding template)
        {
            return value as BindingBase;
        }

        if (Target is not FundamentalUIComponent part)
        {
            Fail($"{{TemplateBinding {template.Path}}} in {{Localize {Localize.Key}}} is outside a template");
            return null;
        }

        // A part's bindings are made before the template is stamped onto its control, so the control may not be known
        // yet - and is the moment it is.
        if (_waitingOn == null)
        {
            _waitingOn = part;
            part.TemplatedParentChanged += OnStamped;
        }

        return part.TemplatedParent is { } control ? new Binding(template.Path) { Source = control } : null;
    }

    private void OnStamped(object sender, EventArgs e) => EstablishConnection();

    public override void CloseConnection()
    {
        Languages.Unfollow(this);
        if (_waitingOn != null)
        {
            _waitingOn.TemplatedParentChanged -= OnStamped;
            _waitingOn = null;
        }

        foreach (var argument in _arguments.Values.Append(_key).Where(a => a != null))
        {
            argument.ValueChanged -= OnArgumentChanged;
            argument.CloseConnection();
        }

        _arguments.Clear();
        _key = null;
    }

    public override void UpdateTarget()
    {
        var text = Compose();
        if (TargetProperty != null && Target != null)
        {
            Target.SetValue(TargetProperty, text, ValuePriority.Binding);
        }
        else
        {
            ProducedValue = text;
            RaiseValueChanged();
        }
    }

    private void OnArgumentChanged(BindingExpressionBase argument)
    {
        if (!_connecting)
        {
            ScheduleUpdate();
        }
    }

    private object Compose()
    {
        var table = Localize.Table;
        if (table == null)
        {
            Fail($"{{Localize {Localize.Key}}} has no table");
            return Localize.FallbackValue;
        }

        // A key read from a binding names a kind of thing, and a name the table has no word for is said as it is - an
        // application's own tool keeps the name it was given. Such a word takes the arguments it uses and leaves the rest.
        if (Localize.KeySource != null)
        {
            var key = _key?.ProducedValue?.ToString();
            Status = BindingStatus.Active;
            if (string.IsNullOrEmpty(key))
            {
                return Localize.TargetNullValue;
            }

            return table.TryText(key, Argument, FormatCulture, out var word) ? word : key;
        }

        if (!table.TryText(Localize.Key, Argument, FormatCulture, out var text))
        {
            Fail($"{table.GetType().Name} has no string '{Localize.Key}'");
            return Localize.FallbackValue;
        }

        var placeholders = ((ILanguageTable)table).PlaceholdersOf(Localize.Key);
        if (Localize.Arguments.Keys.FirstOrDefault(n => !placeholders.Contains(n)) is { } unknown)
        {
            Fail($"{table.GetType().Name}.{Localize.Key} has no placeholder '{unknown}'");
        }

        Status = BindingStatus.Active;
        return text ?? Localize.TargetNullValue;
    }

    private object Argument(string name)
    {
        if (_arguments.TryGetValue(name, out var argument))
        {
            return argument.ProducedValue;
        }

        // A value given as it is; one that follows something not reached yet has none yet.
        return Localize.Arguments.TryGetValue(name, out var value) && value is not (Data.BindingBase or TemplateBinding) ? value : null;
    }
}
