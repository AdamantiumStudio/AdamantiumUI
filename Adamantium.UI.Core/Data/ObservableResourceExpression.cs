using System;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Data;

/// <summary>The live connection behind <c>{ObservableResource Key}</c>: resolves a keyed resource tree-scoped (Local, then
/// Theme, then Global) and re-resolves on any resource-set change or re-parent.</summary>
public class ObservableResourceExpression : BindingExpressionBase
{
    private readonly string _key;
    private readonly ValuePriority _priority;
    private readonly object _token;
    private IResourceManager _resources;

    public ObservableResourceExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty, string key,
        ValuePriority priority = ValuePriority.Template, object token = null)
    {
        Target = target;
        TargetProperty = targetProperty;
        _key = key;
        _priority = priority;
        _token = token;
    }

    public override void EstablishConnection()
    {
        CloseConnection();
        _resources = UIAppContext.Current?.ResourceManager;
        if (_resources == null || TargetProperty == null || string.IsNullOrEmpty(_key)) return;

        UpdateTarget();
        _resources.ResourcesChanged += OnResourcesChanged;

        // Re-resolve once the target is ROOTED, where its full ancestor chain - incl. a Local dictionary's owner - is
        // present: a visual element via the visual tree; a non-visual one via the LOGICAL tree (it never enters the
        // visual tree, so its visual-attach event never fires).
        // Asked of the nearest ELEMENT - a non-tree target (a Transform) has no attach of its own and rides its owner's.
        var element = DataContextSource;
        if (element is IUIComponent visual) visual.AttachedToVisualTreeEvent += OnVisualAttached;
        else if (element != null) element.AttachedToLogicalTree += OnLogicalAttached;

        if (Target is IInputComponent input) input.Unloaded += OnTargetUnloaded;
    }

    public override void UpdateTarget()
    {
        if (_resources == null || TargetProperty == null) return;
        var value = _resources.FindResource(DataContextSource, _key);   // tree-scoped; falls back Theme -> Global
        // A transient miss (e.g. mid theme-swap, before the new theme's dictionary is loaded) must NOT clobber the
        // property with null - keep the last good value until the resolve succeeds again.
        if (value == null) return;
        if (_priority == ValuePriority.Trigger && _token != null)
            Target.SetTriggerValue(TargetProperty, value, _token);
        else
            Target.SetValue(TargetProperty, value, _priority);
    }

    public override void CloseConnection()
    {
        if (_resources != null)
        {
            _resources.ResourcesChanged -= OnResourcesChanged;
            _resources = null;
        }
        var element = DataContextSource;
        if (element is IUIComponent visual) visual.AttachedToVisualTreeEvent -= OnVisualAttached;
        else if (element != null) element.AttachedToLogicalTree -= OnLogicalAttached;
        if (Target is IInputComponent input) input.Unloaded -= OnTargetUnloaded;
    }

    private void OnResourcesChanged(object sender, EventArgs e) => UpdateTarget();
    private void OnVisualAttached(object sender, VisualTreeAttachmentEventArgs e) => UpdateTarget();
    private void OnLogicalAttached(object sender, LogicalTreeAttachmentEventArgs e) => UpdateTarget();
    private void OnTargetUnloaded(object sender, RoutedEventArgs e) => CloseConnection();
}
