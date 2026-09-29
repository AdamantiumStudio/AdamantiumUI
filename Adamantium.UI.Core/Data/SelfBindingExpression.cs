namespace Adamantium.UI.Core.Data;

/// <summary>
/// Live connection for <c>{Self ...}</c>: binds a target property to another property (or dotted path) on the SAME
/// element. No tree walk - the source is always the target itself. The path from there on is the one a
/// <see cref="BindingExpression"/> follows, as it is for <see cref="AncestorBindingExpression"/>.
/// </summary>
public class SelfBindingExpression : BindingExpressionBase
{
    private readonly Self _def;
    internal ValuePriority Priority { get; set; } = ValuePriority.Binding;
    private BindingExpression _path;
    private bool _connected;

    public SelfBindingExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty, Self def)
    {
        Target = target;
        TargetProperty = targetProperty;
        _def = def;
    }

    public override void EstablishConnection()
    {
        CloseConnection();
        if (Target == null) return;

        if (!AncestorBindingExpression.HasFirstLink(Target, _def.Path))
        {
            Fail($"{{Self}} on {Target.GetType().Name}.{TargetProperty?.Name}: no '{_def.Path}' property on the element.");
            return;
        }

        _path ??= new BindingExpression(Target, TargetProperty, new Binding(_def.Path)
        {
            Source = Target,
            Mode = _def.Mode,
            Converter = _def.Converter,
            ConverterParameter = _def.ConverterParameter,
            FallbackValue = _def.FallbackValue,
            TargetNullValue = _def.TargetNullValue
        }) { Priority = Priority };
        _path.EstablishConnection();
        _connected = true;
        Status = _path.Status;
    }

    public override void CloseConnection()
    {
        BindingUpdateQueue.Remove(this);
        _path?.Forget();
        _connected = false;
    }

    public override void UpdateTarget()
    {
        if (_connected)
        {
            _path.UpdateTarget();
        }
    }

    public override void UpdateSource()
    {
        if (_connected)
        {
            _path.UpdateSource();
        }
    }
}
