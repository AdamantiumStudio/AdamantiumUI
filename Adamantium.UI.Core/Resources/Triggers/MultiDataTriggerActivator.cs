using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Resources.Triggers;

/// <summary>Active only while ALL of a <see cref="MultiDataTrigger"/>'s bindings resolve to their values. The bindings are
/// one producer <see cref="MultiBinding"/>, which hands over the values of all of them once each is connected - so another
/// DataContext is read whole, never half old and half new. It, or the host's DataContext, changing re-evaluates the AND.</summary>
internal sealed class MultiDataTriggerActivator : TriggerActivatorBase
{
    private readonly IFundamentalUIComponent _host;
    private readonly MultiBindingExpression _expression;
    private readonly object[] _values;

    public MultiDataTriggerActivator(ITriggerExecutionContext context, MultiDataTrigger blueprint) : base(context, blueprint)
    {
        _host = context.HostComponent;
        var conditions = blueprint.Conditions;
        if (_host == null || conditions.Count == 0)
        {
            return;
        }

        var bindings = new MultiBinding();
        var values = new object[conditions.Count];
        for (var i = 0; i < conditions.Count; i++)
        {
            // A condition with no binding can never hold: the trigger stays inert, as a MultiTrigger with an unknown
            // property does.
            if (conditions[i].Binding == null)
            {
                return;
            }

            bindings.Bindings.Add(conditions[i].Binding);
            values[i] = conditions[i].Value;
        }

        _expression = new MultiBindingExpression(_host, null, bindings);
        _values = values;
    }

    public override void Activate()
    {
        if (_expression == null) return;
        _expression.ValueChanged += OnProducedValueChanged;
        _host.DataContextChanged += OnDataContextChanged;
        _expression.EstablishConnection();
    }

    public override void Deactivate()
    {
        if (_expression == null) return;
        _expression.ValueChanged -= OnProducedValueChanged;
        _host.DataContextChanged -= OnDataContextChanged;
        _expression.CloseConnection();
        TearDown();
    }

    private void OnDataContextChanged(object sender, AdamantiumPropertyChangedEventArgs e) => _expression.EstablishConnection();

    private void OnProducedValueChanged(BindingExpressionBase _) => Evaluate();

    private void Evaluate()
    {
        if (_expression.ProducedValue is not object[] produced || produced.Length != _values.Length)
        {
            ApplyState(false);
            return;
        }

        for (var i = 0; i < _values.Length; i++)
        {
            if (!DataTriggerActivator.Matches(produced[i], _values[i]))
            {
                ApplyState(false);
                return;
            }
        }

        ApplyState(true);
    }
}
