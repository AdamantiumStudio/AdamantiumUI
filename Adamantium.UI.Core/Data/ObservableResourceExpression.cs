using System;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Data;

/// <summary>The live connection behind <c>{ObservableResource Key}</c>: resolves a keyed resource tree-scoped (Local, then
/// Theme, then Global) and re-resolves on any resource-set change or re-parent - and, with a key taken from a binding,
/// whenever the binding names another one. A miss of the key already shown keeps its value (a theme swap passes through
/// one); a new key that resolves to nothing, or no key, clears it.</summary>
public class ObservableResourceExpression : BindingExpressionBase
{
    private readonly string _key;
    private readonly BindingBase _keySource;
    private readonly ValuePriority _priority;
    private readonly object _token;
    private IResourceManager _resources;
    private BindingExpressionBase _followedKey;
    private IFundamentalUIComponent _element;
    private string _shownKey;

    public ObservableResourceExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty, string key,
        ValuePriority priority = ValuePriority.Template, object token = null, BindingBase keySource = null)
    {
        Target = target;
        TargetProperty = targetProperty;
        _key = key;
        _keySource = keySource;
        _priority = priority;
        _token = token;
    }

    public override void EstablishConnection()
    {
        CloseConnection();
        _resources = UIAppContext.Current?.ResourceManager;
        if (_resources == null || TargetProperty == null || (_keySource == null && string.IsNullOrEmpty(_key))) return;

        _element = DataContextSource;

        if (_keySource != null)
        {
            FollowKey();
            if (_element != null)
            {
                _element.DataContextChanged += OnDataContextChanged;
            }
        }

        UpdateTarget();
        _resources.ResourcesChanged += OnResourcesChanged;

        if (_element is IUIComponent visual) visual.AttachedToVisualTreeEvent += OnVisualAttached;
        else if (_element != null) _element.AttachedToLogicalTree += OnLogicalAttached;

        if (Target is IInputComponent input) input.Unloaded += OnTargetUnloaded;
    }

    public override void UpdateTarget()
    {
        if (_resources == null || TargetProperty == null) return;

        var key = CurrentKey();
        var value = string.IsNullOrEmpty(key) ? null : _resources.FindResource(DataContextSource, key);
        if (value == null)
        {
            if (key != _shownKey) ClearShown();
            return;
        }

        _shownKey = key;
        if (_priority == ValuePriority.Trigger && _token != null)
            Target.SetTriggerValue(TargetProperty, value, _token);
        else
            Target.SetValue(TargetProperty, value, _priority);
    }

    public override void CloseConnection()
    {
        UnfollowKey();

        if (_resources != null)
        {
            _resources.ResourcesChanged -= OnResourcesChanged;
            _resources = null;
        }

        if (_element != null)
        {
            _element.DataContextChanged -= OnDataContextChanged;
            if (_element is IUIComponent visual) visual.AttachedToVisualTreeEvent -= OnVisualAttached;
            else _element.AttachedToLogicalTree -= OnLogicalAttached;
            _element = null;
        }

        if (Target is IInputComponent input) input.Unloaded -= OnTargetUnloaded;
        if (Target is IUIComponent returning) returning.AttachedToVisualTreeEvent -= OnReturned;
    }

    private string CurrentKey() => _keySource != null ? _followedKey?.ProducedValue?.ToString() : _key;

    private void ClearShown()
    {
        if (_shownKey == null) return;

        _shownKey = null;
        if (_priority == ValuePriority.Trigger && _token != null)
            Target.ClearTriggerValue(TargetProperty, _token);
        else
            Target.ClearValue(TargetProperty, _priority);
    }

    private void FollowKey()
    {
        UnfollowKey();
        _followedKey = _keySource.CreateExpression(Target, null);
        _followedKey.ValueChanged += OnKeyChanged;
        _followedKey.EstablishConnection();
    }

    private void UnfollowKey()
    {
        if (_followedKey == null)
        {
            return;
        }

        _followedKey.ValueChanged -= OnKeyChanged;
        _followedKey.CloseConnection();
        _followedKey = null;
    }

    private void OnDataContextChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        FollowKey();
        UpdateTarget();
    }

    private void OnResourcesChanged(object sender, EventArgs e) => UpdateTarget();

    private void OnKeyChanged(BindingExpressionBase key) => UpdateTarget();

    private void OnVisualAttached(object sender, VisualTreeAttachmentEventArgs e) => UpdateTarget();

    private void OnLogicalAttached(object sender, LogicalTreeAttachmentEventArgs e) => UpdateTarget();

    private void OnTargetUnloaded(object sender, RoutedEventArgs e)
    {
        CloseConnection();
        if (Target is IUIComponent visual) visual.AttachedToVisualTreeEvent += OnReturned;
    }

    private void OnReturned(object sender, VisualTreeAttachmentEventArgs e) => EstablishConnection();
}
