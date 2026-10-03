using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Core.Data;

/// <summary>A live <c>{Binding}</c> from <see cref="Binding.Source"/> or the DataContext through a dotted path to a target
/// property. With no target property it runs as a producer for a <see cref="MultiBinding"/>.</summary>
public class BindingExpression : BindingExpressionBase
{
   public object ResolvedSource { get; private set; }
   public string SourcePropertyName { get; private set; }

   /// <inheritdoc/>
   public override bool IsResolved => ResolvedSource != null && (_bindToSource || _sourceProperty != null || _leaf != null);

   /// <inheritdoc/>
   public override Type SourceType => _leaf?.Type ?? _sourceProperty?.PropertyType;

   /// <inheritdoc/>
   public override bool IsSourceEdited
   {
      get
      {
         var (component, property) = SourceSlot();

         return property != null && (component.IsSet(property, ValuePriority.Local) || HoldsWrittenCurrentValue(component, property));
      }
   }

   /// <inheritdoc/>
   public override bool ResetSource()
   {
      var (component, property) = SourceSlot();
      if (property == null) return false;

      component.ClearValue(property);
      if (HoldsWrittenCurrentValue(component, property))
      {
         component.ClearValue(property, ValuePriority.Binding);
      }

      return true;
   }

   private static bool HoldsWrittenCurrentValue(AdamantiumComponent component, AdamantiumProperty property)
   {
      return component.IsSet(property, ValuePriority.Binding) && BindingEngine.GetBindingExpression(component, property) == null;
   }

   // The property system's own slot behind the path, where there is one. A plain object's property has none - there is
   // nothing there that knows what "untouched" means - and the answer is then nothing.
   private (AdamantiumComponent Component, AdamantiumProperty Property) SourceSlot()
   {
      // A path ending inside a struct has no slot of its own: the slot belongs to the WHOLE value, and putting one
      // number back by clearing all of them is not what the line says it does.
      if (_leaf != null) return (null, null);
      if (ResolvedSource is not AdamantiumComponent component || SourcePropertyName == null) return (null, null);

      return (component, component.GetProperty(SourcePropertyName));
   }

   private PropertyInfo _sourceProperty;
   private Func<object, object> _sourceGetter;   // compiled reader for _sourceProperty (the hot ComputeValue path)
   private bool _bindToSource;   // empty path ({Binding}, {Binding ElementName=x}) -> the value IS the resolved source object
   private INotifyPropertyChanged _observed;
   private AdamantiumComponent _observedComponent;   // element source ({ElementName}) - observed via AdamantiumProperty changes, not INPC
   private string[] _segments;   // cached Binding.Path split on '.', computed once (path is fixed per expression)
   private List<(object Owner, string Segment)> _passed;
   private bool _reconnectPending;
   private IUIComponent _awaitingAttach;

   internal ValuePriority Priority { get; set; } = ValuePriority.Binding;

   internal static PropertyInfo FindProperty(Type type, string name)
   {
      for (var declaring = type; declaring != null; declaring = declaring.BaseType)
      {
         foreach (var property in declaring.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
         {
            if (property.Name == name && property.GetIndexParameters().Length == 0)
            {
               return property;
            }
         }
      }

      // ...then what the object has through an interface: a member an interface implements by default belongs to the
      // object as much as one its class declares, and reading it through the interface reaches whichever answers.
      foreach (var contract in type.GetInterfaces())
      {
         if (contract.GetProperty(name) is { } property && property.GetIndexParameters().Length == 0)
         {
            return property;
         }
      }

      return null;
   }

   // Property-accessor cache: `GetType().GetProperty(name)` is a slow metadata search and `PropertyInfo.GetValue` a slow
   // reflection invoke, and a virtualized list re-runs both for EVERY binding of EVERY recycled row on scroll (a fling can
   // rebind hundreds of rows x ~15 bindings/frame). Cache the PropertyInfo + a COMPILED getter delegate per (type, name)
   // so a rebind is a dictionary hit + a near-native call instead. Shared across all bindings, populated once per shape.
   private static readonly ConcurrentDictionary<(Type, string), (PropertyInfo Prop, Func<object, object> Getter)> _accessors = new();

   private static (PropertyInfo Prop, Func<object, object> Getter) GetAccessor(Type type, string name)
      => _accessors.GetOrAdd((type, name), static key =>
      {
         var prop = FindProperty(key.Item1, key.Item2);
         if (prop is not { CanRead: true }) return (prop, null);
         var o = Expression.Parameter(typeof(object), "o");
         var owner = prop.DeclaringType is { IsInterface: true } contract ? contract : key.Item1;
         var body = Expression.Convert(Expression.Property(Expression.Convert(o, owner), prop), typeof(object));
         return (prop, Expression.Lambda<Func<object, object>>(body, o).Compile());
      });

   // A PATH THAT ENDS INSIDE A STRUCT (`Offset.X`, `Bounds.X`): the walk reaches a box - a copy made on the way - so a
   // read is frozen at the moment the path resolved and a write lands in the copy and is dropped, both silently. Such
   // a path is therefore walked fresh on every read and written back UP, and it is watched at the object the path
   // STARTS at, by its first segment: the box has nothing to announce.
   private sealed record Hop(Type Type, bool CanWrite, Func<object, object> Get, Action<object, object> Set);

   private static readonly ConcurrentDictionary<(Type, string), Hop> _hops = new();

   // Fields as well as properties: the maths types are fields (Vector2.X), and a path that cannot see them cannot see
   // a position.
   private static Hop HopTo(Type type, string name) => _hops.GetOrAdd((type, name), static key =>
   {
      var (owner, member) = key;
      var o = Expression.Parameter(typeof(object), "o");
      var self = Expression.Convert(o, owner);

      if (FindProperty(owner, member) is { } prop)
      {
         var from = prop.DeclaringType is { IsInterface: true } contract ? Expression.Convert(o, contract) : self;
         var read = prop.CanRead
            ? Expression.Lambda<Func<object, object>>(Expression.Convert(Expression.Property(from, prop), typeof(object)), o).Compile()
            : null;

         return new Hop(prop.PropertyType, prop.CanWrite, read, prop.SetValue);
      }

      if (owner.GetField(member) is { } field)
      {
         var read = Expression.Lambda<Func<object, object>>(
            Expression.Convert(Expression.Field(self, field), typeof(object)), o).Compile();

         return new Hop(field.FieldType, !field.IsInitOnly, read, field.SetValue);
      }

      return null;
   });

   private Hop _leaf;      // set only for a struct-ended path; the fast path leaves it null
   private object[] _boxes;   // what each segment was read from, reused between walks

   public Binding Binding { get; set; }
   public BindingMode Mode { get; set; }

   private bool IsProducer => TargetProperty == null;

   private bool FollowsCulture =>
      !string.IsNullOrEmpty(BindingBase.StringFormat) || Binding.Converter != null || BindingBase.Culture != null ||
      TargetProperty.PropertyType == typeof(string);

   public BindingExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty, BindingBase bindingBase)
   {
      Target = target;
      TargetProperty = targetProperty;
      BindingBase = bindingBase;
      Binding = (Binding)bindingBase;

      // Default means "whatever this PROPERTY says" (PropertyMetadataOptions.BindsTwoWayByDefault). Read HERE because
      // nothing else read it: the metadata carried DefaultBindingMode for 67 properties and no expression ever asked,
      // so every {Binding} without an explicit Mode was silently one-way.
      // A producer (a trigger's condition, a MultiBinding child) has no target property to ask, and stays as written.
      Mode = Binding.Mode == BindingMode.Default && target != null && targetProperty != null
         ? targetProperty.GetDefaultMetadata(target.GetType()).DefaultBindingMode
         : Binding.Mode;
   }

   public BindingExpression(IAdamantiumComponent target, string targetPropertyName, BindingBase bindingBase)
      : this(target, target.GetProperty(targetPropertyName), bindingBase)
   {
   }

   // The single place that turns a BindingBase into a live, connected expression; each kind of binding makes its own.
   public static BindingExpressionBase CreateBindingExpression(IAdamantiumComponent target,
      AdamantiumProperty targetProperty, BindingBase bindingBase)
   {
      var expression = bindingBase.CreateExpression(target, targetProperty);
      expression.EstablishConnection();
      return expression;
   }

   public static BindingExpressionBase CreateBindingExpression(IAdamantiumComponent target,
      string targetPropertyName, BindingBase bindingBase)
      => CreateBindingExpression(target, target.GetProperty(targetPropertyName), bindingBase);

   public override void EstablishConnection()
   {
      if (!IsProducer && FollowsCulture)
      {
         Languages.Follow(this);
      }

      // Re-resolve against the (possibly new) DataContext BEFORE touching subscriptions.
      var previousObserved = _observed;
      UnwatchPassed();
      ResolveSource();
      var newObserved = ResolvedSource as INotifyPropertyChanged;

      // Same source object (a shared sub-view-model): keep the subscription, since re-subscribing on a busy source is
      // O(subscribers) per rebind.
      if (ReferenceEquals(newObserved, previousObserved) && previousObserved != null)
      {
         WatchPassed();

         // The target already holds this value, so skip the re-push; a producer still republishes for its parent.
         if (IsProducer) Refresh();
         return;
      }

      // Source object genuinely changed: tear down the old subscription and establish the new one.
      ReleaseSource();

      // OneWayToSource: the flow is TARGET -> SOURCE only. Never observe or push the source; write its initial value from
      // the target, then update it whenever the target changes (a target with a read-only/private-set property that can't
      // be bound TwoWay - e.g. reading a control's SelectedItem out into a view-model).
      if (Mode == BindingMode.OneWayToSource && !IsProducer)
      {
         UpdateSource();
         if (Target != null) Target.PropertyChanged += OnTargetPropertyChanged;
         WatchPassed();
         return;
      }

      Refresh();                    // initial push (or produce)
      if (newObserved != null)
      {
         _observed = newObserved;
         SharedSourceRegistry.Subscribe(newObserved, this);   // one shared subscription per source, O(1) add (see SharedSourceRegistry)
      }
      // Element source ({ElementName}): a UI element does not implement INotifyPropertyChanged - its properties change
      // through the AdamantiumProperty system. Observe THAT so source->target updates flow (a wheel-zoom moving a slider
      // bound to ZoomBox.ScaleX, a value readout, ...). Not shared (element bindings aren't the virtualized-list hot path).
      if (newObserved == null && ResolvedSource is AdamantiumComponent component)
      {
         _observedComponent = component;
         component.PropertyChanged += OnSourceComponentChanged;
         System.Threading.Interlocked.Increment(ref SourceHooks);
      }
      if (Mode == BindingMode.TwoWay && !IsProducer && Target != null)
         Target.PropertyChanged += OnTargetPropertyChanged;
      WatchPassed();
   }

   public override void CloseConnection()
   {
      Languages.Unfollow(this);
      UnwatchPassed();
      _passed?.Clear();
      StopAwaitingAttach();
      ReleaseSource();
   }

   internal override void Retry()
   {
      CloseConnection();
      EstablishConnection();
   }

   internal void Forget()
   {
      CloseConnection();
      ForgetResolution();
   }

   private void ReleaseSource()
   {
      BindingUpdateQueue.Remove(this);   // F2: a closed binding must not be applied by a later flush
      _reconnectPending = false;
      if (_observed != null)
      {
         SharedSourceRegistry.Unsubscribe(_observed, this);   // O(1) remove - the reason a shrunk window can release cheaply
         _observed = null;
      }
      if (_observedComponent != null)
      {
         _observedComponent.PropertyChanged -= OnSourceComponentChanged;
         System.Threading.Interlocked.Increment(ref SourceUnhooks);
         _observedComponent = null;
      }
      if ((Mode == BindingMode.TwoWay || Mode == BindingMode.OneWayToSource) && !IsProducer && Target != null)
         Target.PropertyChanged -= OnTargetPropertyChanged;
   }

   private void WatchPassed()
   {
      if (_passed == null)
      {
         return;
      }

      foreach (var (owner, _) in _passed)
      {
         if (owner is AdamantiumComponent component)
         {
            component.PropertyChanged += OnPassedComponentChanged;
         }

         if (owner is INotifyPropertyChanged notifying && !ReferenceEquals(owner, _observed))
         {
            SharedSourceRegistry.Subscribe(notifying, this);
         }
      }
   }

   private void UnwatchPassed()
   {
      if (_passed == null)
      {
         return;
      }

      foreach (var (owner, _) in _passed)
      {
         if (owner is AdamantiumComponent component)
         {
            component.PropertyChanged -= OnPassedComponentChanged;
         }

         if (owner is INotifyPropertyChanged notifying && !ReferenceEquals(owner, _observed))
         {
            SharedSourceRegistry.Unsubscribe(notifying, this);
         }
      }
   }

   private void OnPassedComponentChanged(object sender, AdamantiumPropertyChangedEventArgs e)
   {
      if (_writingSource || !Passes(sender, e.Property?.Name))
      {
         return;
      }

      _reconnectPending = true;
      ScheduleUpdate();
   }

   private bool Passes(object sender, string propertyName)
   {
      if (_passed == null)
      {
         return false;
      }

      foreach (var (owner, segment) in _passed)
      {
         if (ReferenceEquals(owner, sender) && (string.IsNullOrEmpty(propertyName) || propertyName == segment))
         {
            return true;
         }
      }

      return false;
   }

   private void ForgetResolution()
   {
      ResolvedSource = null;
      _sourceProperty = null;
      _sourceGetter = null;
      SourcePropertyName = null;
      _bindToSource = false;
      _leaf = null;
      _passed?.Clear();
      if (_boxes != null)
      {
         Array.Clear(_boxes);
      }
   }

   // Source = explicit Binding.Source, else the target's DataContext. Walk all but the last path segment to reach
   // the object that owns the bound property; the leaf segment is the property we read/observe.
   private void ResolveSource()
   {
      ForgetResolution();
      StopAwaitingAttach();
      Status = BindingStatus.Active;

      var named = Binding.Source == null && !string.IsNullOrEmpty(Binding.ElementName);
      var root = Binding.Source ?? (named ? ResolveElementName() : DataContextSource?.DataContext);
      var path = Binding.Path?.Path;
      if (root == null)
      {
         if (named && AnchorElement(Target) is { } anchor)
         {
            if (anchor.IsAttachedToVisualTree)
            {
               Fail($"{Target?.GetType().Name}.{TargetProperty?.Name}: no element named '{Binding.ElementName}' in the tree.");
            }
            else
            {
               AwaitAttach(anchor);
            }
         }

         return;
      }

      // Empty path with a source -> bind to the SOURCE OBJECT ITSELF ({Binding}, {Binding ElementName=x}, {Binding Source=y}),
      // the standard WPF behavior. There is no leaf property to read/observe - the value simply IS the resolved source.
      if (string.IsNullOrEmpty(path))
      {
         ResolvedSource = root;
         _bindToSource = true;
         return;
      }

      // Split ONCE per expression, not per resolve: the path is fixed for the binding's life, but ResolveSource runs on
      // every DataContext change (every rebind of every recycled tile) - a fresh string[] alloc per call was steady GC
      // churn on the scroll hot path.
      var segments = _segments ??= path.Split('.');
      object current = root;
      for (var i = 0; i < segments.Length - 1 && current != null; i++)
      {
         if (current is INotifyPropertyChanged or AdamantiumComponent)
         {
            (_passed ??= []).Add((current, segments[i]));
         }

         var accessor = GetAccessor(current.GetType(), segments[i]);
         if (accessor.Prop != null)
         {
            current = accessor.Getter?.Invoke(current);
         }
         else if (HopTo(current.GetType(), segments[i]) is { } field)
         {
            current = field.Get(current);
         }
         else
         {
            ReportMissing(current.GetType(), segments[i]);
            return;
         }
      }

      if (current == null)
         return;

      if (current.GetType().IsValueType)
      {
         _leaf = HopTo(current.GetType(), segments[^1]);
         if (_leaf == null)
         {
            ReportMissing(current.GetType(), segments[^1]);
            return;
         }

         _boxes ??= new object[segments.Length];
         ResolvedSource = root;
         SourcePropertyName = segments[0];
         if (_passed is { Count: 1 } && ReferenceEquals(_passed[0].Owner, root))
         {
            _passed.Clear();
         }

         return;
      }

      ResolvedSource = current;
      SourcePropertyName = segments[^1];
      (_sourceProperty, _sourceGetter) = GetAccessor(current.GetType(), SourcePropertyName);
      if (_sourceProperty == null)
      {
         ReportMissing(current.GetType(), SourcePropertyName);
      }
   }

   private void AwaitAttach(IUIComponent anchor)
   {
      _awaitingAttach = anchor;
      anchor.AttachedToVisualTreeEvent += OnAnchorAttached;
   }

   private void StopAwaitingAttach()
   {
      if (_awaitingAttach == null)
      {
         return;
      }

      _awaitingAttach.AttachedToVisualTreeEvent -= OnAnchorAttached;
      _awaitingAttach = null;
   }

   private void OnAnchorAttached(object sender, VisualTreeAttachmentEventArgs e)
   {
      StopAwaitingAttach();
      _reconnectPending = true;
      ScheduleUpdate();
   }

   private void ReportMissing(Type owner, string member)
   {
      Fail($"{Target?.GetType().Name}.{TargetProperty?.Name}: path '{Binding.Path?.Path}' breaks at '{member}' - {owner.Name} has no such property.");
   }

   // {Binding Path, ElementName=X}: the source is the element named X in the target's tree (not the DataContext). Resolved
   // by walking to the tree root and searching for a matching Name - re-runs with ResolveSource on every DataContext change
   // (attach), so a forward-referenced element resolves once the whole tree exists. Null until then.
   private object ResolveElementName()
   {
      if (string.IsNullOrEmpty(Binding.ElementName)) return null;

      // A NON-VISUAL target - a brush, a pen, a gradient stop - has no place in the tree to search FROM, so it borrows
      // the element that holds it (the same InheritanceParent a resource lookup uses; see Brush.Anchor). Without this
      // an ElementName written on a brush resolved to nothing, silently - and a VisualBrush cannot name its source at
      // all in markup, which is the only way to write one.
      var target = AnchorElement(Target);
      if (target == null) 
         return null;

      // The TEMPLATE first, when this binding was written inside one. A template is its own namescope - its names belong
      // to the control that applied it, not to the window - so they are not on the visual tree under Name and the walk
      // below cannot see them. Without this an ElementName inside a ControlTemplate silently resolved to nothing, which
      // is a binding that never reports a problem and simply never has a value.
      if (target.TemplatedParent is ITemplateHost host && host.GetTemplateChild(Binding.ElementName) is IUIComponent inTemplate)
         return inTemplate;

      for (IFundamentalUIComponent node = target; node != null; node = (node as IUIComponent)?.VisualParent ?? node.LogicalParent)
      {
         if (NameScope.Find(node, Binding.ElementName) is { } declared)
         {
            return declared;
         }
      }

      var root = target;
      while (root.VisualParent is { } parent) root = parent;
      return FindByName(root, Binding.ElementName, []);
   }

   // The element a search starts from: the target itself when it is one, otherwise the nearest one holding it.
   private static IUIComponent AnchorElement(object target)
   {
      for (var node = target as IAdamantiumComponent; node != null; node = node.InheritanceParent)
      {
         if (node is IUIComponent element) return element;
      }

      return null;
   }

   private static IFundamentalUIComponent FindByName(IFundamentalUIComponent node, string name,
      HashSet<IFundamentalUIComponent> searched)
   {
      if (!searched.Add(node))
      {
         return null;
      }

      if (node.Name == name)
      {
         return node;
      }

      if (node is IUIComponent visual)
      {
         foreach (var child in visual.VisualChildren)
         {
            if (FindByName(child, name, searched) is { } found)
            {
               return found;
            }
         }
      }

      foreach (var child in node.LogicalChildren)
      {
         if (FindByName(child, name, searched) is { } found)
         {
            return found;
         }
      }

      return null;
   }

   // True while UpdateSource writes the source, so this expression does not echo its own write back a frame later.
   private bool _writingSource;

   // THE SOURCE SPOKE WHILE WE WERE WRITING IT. Not the echo of our own value - that one is thrown away - but the fact
   // that it said anything at all, which is the difference between a source that ANSWERED the write with another value
   // and one that simply ignored it. See UpdateSource.
   private bool _sourceSpoke;

   // Called by SharedSourceRegistry (the source's single fan-out handler), not subscribed directly.
   internal void OnSourcePropertyChanged(object sender, PropertyChangedEventArgs e)
   {
      var ours = string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == SourcePropertyName;

      if (_writingSource)   // our own TwoWay write-back - don't echo it back to the target
      {
         if (ours) _sourceSpoke = true;
         return;
      }

      if (Passes(sender, e.PropertyName))
      {
         _reconnectPending = true;
         ScheduleUpdate();
         return;
      }

      // F2: a runtime source change is batched + coalesced (applied once per frame), not pushed synchronously.
      if (ours) ScheduleUpdate();
   }

   // Element source ({ElementName}) property changed via the AdamantiumProperty system - push to the target if it's the
   // property we bind.
   private void OnSourceComponentChanged(object sender, AdamantiumPropertyChangedEventArgs e)
   {
      var ours = e.Property?.Name == SourcePropertyName;

      if (_writingSource)   // our own TwoWay write-back - don't echo it back to the target
      {
         if (ours) _sourceSpoke = true;
         return;
      }

      if (ours) ScheduleUpdate();
   }

   // F2: the coalesced apply reads the current source value (producer mode publishes ProducedValue, top-level pushes
   // to the target) - same path as a source change, just deferred to the per-frame flush.
   internal override void ApplyPending()
   {
      if (!_reconnectPending)
      {
         Refresh();
         return;
      }

      _reconnectPending = false;
      CloseConnection();
      EstablishConnection();
   }

   private void OnTargetPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
   {
      if (Mode is BindingMode.TwoWay or BindingMode.OneWayToSource && e.Property == TargetProperty)
         UpdateSource();
   }

   // Top-level: push to the target property. Producer: publish ProducedValue for a parent MultiBinding.
   private void Refresh()
   {
      if (IsProducer)
      {
         // A producer feeds a trigger's condition or a MultiBinding's converter, and both of those compare against
         // NULL, not against the engine's unset token. The distinction is the target property's business, and a
         // producer has none.
         var produced = ComputeValue(typeof(object));
         ProducedValue = ReferenceEquals(produced, AdamantiumProperty.UnsetValue) ? null : produced;
         RaiseValueChanged();
      }
      else
      {
         UpdateTarget();
      }
   }

   // Reads the source through the converter, applying FallbackValue/TargetNullValue. Returns UNSET, not null, when
   // nothing resolved; a resolved null is a value.
   private object ComputeValue(Type targetType) => Formatted(ReadValue(targetType), targetType);

   // StringFormat lived on every binding, traveled into every expression, and was read by NOBODY except MultiBinding:
   // a single binding took the format, ignored it and showed the raw value without a word. Only where it can mean
   // something - a string target, a value that exists. A PRODUCER (trigger condition, MultiBinding input) asks for
   // object and must keep its type: a comparison against a formatted string is not the comparison that was written.
   private object Formatted(object value, Type targetType)
   {
      if (targetType != typeof(string) || string.IsNullOrEmpty(BindingBase.StringFormat)) return value;
      if (value == null || ReferenceEquals(value, AdamantiumProperty.UnsetValue)) return value;

      return string.Format(FormatCulture, BindingBase.StringFormat, value);
   }

   private object ReadValue(Type targetType)
   {
      // Empty-path binding: the value is the resolved source object itself (optionally run through the converter).
      if (_bindToSource)
      {
         if (ResolvedSource == null)
            return BindingBase.TargetNullValue ?? BindingBase.FallbackValue ?? AdamantiumProperty.UnsetValue;

         return Binding.Converter != null ? ConvertCached(ResolvedSource, targetType) : ResolvedSource;
      }
      if (_leaf != null)
      {
         if (!Walk()) return BindingBase.FallbackValue ?? AdamantiumProperty.UnsetValue;

         var read = _leaf.Get(_boxes[^1]);
         if (Binding.Converter != null) read = ConvertCached(read, targetType);
         return read ?? BindingBase.TargetNullValue ?? BindingBase.FallbackValue;
      }
      if (_sourceProperty == null) return BindingBase.FallbackValue ?? AdamantiumProperty.UnsetValue;
      var value = _sourceGetter != null ? _sourceGetter(ResolvedSource) : _sourceProperty.GetValue(ResolvedSource);
      if (Binding.Converter != null)
         value = ConvertCached(value, targetType);
      if (value == null)
         value = BindingBase.TargetNullValue ?? BindingBase.FallbackValue;
      return value;
   }

   // Converted-value cache weakly keyed by source object: one instance per item across rebinds, re-converted when the
   // raw input changes. Value-type sources convert directly.
   private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object,
      Dictionary<(IValueConverter, Type, string, CultureInfo), (object Raw, object Converted)>> _convertCache = new();

   private object ConvertCached(object raw, Type targetType)
   {
      var culture = FormatCulture;
      var source = ResolvedSource;
      if (source == null || source.GetType().IsValueType)
         return Binding.Converter.Convert(raw, targetType, Binding.ConverterParameter, culture);

      var cache = _convertCache.GetOrCreateValue(source);
      var key = (Binding.Converter, targetType, SourcePropertyName, culture);
      if (cache.TryGetValue(key, out var entry) && Equals(entry.Raw, raw))
         return entry.Converted;

      var converted = Binding.Converter.Convert(raw, targetType, Binding.ConverterParameter, culture);
      cache[key] = (raw, converted);
      return converted;
   }

   public override void UpdateTarget()
   {
      if (TargetProperty == null) return;
      var value = ComputeValue(TargetProperty.PropertyType);
      // Nothing to say - no source, no path, no fallback: leave the target where it is. A resolved null is NOT that; it
      // is the source asking for the property's default back, and refusing to carry it meant no page could ever hand
      // one of the "null = let the theme decide" properties back to the theme from markup.
      if (ReferenceEquals(value, AdamantiumProperty.UnsetValue)) return;
      // ...and a property with no way to HOLD nothing cannot be handed one either. A double has no null to go back to,
      // and the slot would be read as (double)null - the mirror of the rule UpdateSource already applies on the way out.
      if (value == null && !CanHoldNothing(TargetProperty.PropertyType)) return;
      // Can't make the value fit the target type (e.g. a FallbackValue="50" on an ICommand property)? Leave the target
      // at its default instead of pushing an incompatible value, which would throw in SetValue and abort the whole load.
      if (!TryCoerce(value, TargetProperty.PropertyType, out var coerced, FormatCulture)) return;
      Target.SetValue(TargetProperty, coerced, Priority);
      RuntimeStats.BindingUpdatesApplied++;   // diagnostics: a binding wrote its target (initial/establish, DataContext re-resolve, or a batched source change)
   }

   // Walks the path from the root, remembering what each segment was read FROM - the last of them owns the leaf.
   private bool Walk()
   {
      object current = ResolvedSource;

      for (var i = 0; i < _segments.Length; i++)
      {
         if (current == null) return false;

         _boxes[i] = current;
         if (i < _segments.Length - 1) current = HopTo(current.GetType(), _segments[i])?.Get?.Invoke(current);
      }

      return true;
   }

   // The leaf into its box, then each box into whatever held it - until something that is not a copy is reached.
   private void Backfill(object written)
   {
      _leaf.Set(_boxes[^1], written);

      for (var i = _segments.Length - 1; i >= 1; i--)
      {
         if (!_boxes[i].GetType().IsValueType) return;

         var into = HopTo(_boxes[i - 1].GetType(), _segments[i - 1]);
         if (into is not { CanWrite: true }) return;

         if (_boxes[i - 1] is AdamantiumComponent component && component.GetProperty(_segments[i - 1]) is { } property)
         {
            component.SetCurrentValue(property, _boxes[i]);
         }
         else
         {
            into.Set(_boxes[i - 1], _boxes[i]);
         }
      }
   }

   private object ReadSource()
   {
      if (_leaf == null) return _sourceProperty.GetValue(ResolvedSource);

      return Walk() ? _leaf.Get(_boxes[^1]) : null;
   }

   private void WriteSource(object written)
   {
      if (_leaf != null)
      {
         Backfill(written);
         return;
      }

      var (component, property) = SourceSlot();
      if (property != null)
      {
         component.SetCurrentValue(property, written);
         return;
      }

      _sourceProperty.SetValue(ResolvedSource, written);
   }

   public override void UpdateSource()
   {
      var sourceType = _leaf?.Type ?? _sourceProperty?.PropertyType;
      var writable = _leaf?.CanWrite ?? _sourceProperty is { CanWrite: true };

      if (sourceType == null || !writable || TargetProperty == null) return;
      if (_leaf != null && !Walk()) return;

      var targetValue = Target.GetValue(TargetProperty);
      var value = targetValue;
      if (Binding.Converter != null)
         value = Binding.Converter.ConvertBack(value, sourceType, Binding.ConverterParameter, FormatCulture);

      // "No value" cannot be written into a source that has no way to hold it: a NumericUpDown that was cleared has a
      // null Value, and a view-model exposing a plain double would take it as a reflection error mid-keystroke. Leave
      // the source at what it last agreed to instead - and leave our own copy of it alone too, since nothing moved.
      if (value == null && sourceType.IsValueType && Nullable.GetUnderlyingType(sourceType) == null)
      {
         return;
      }

      // Guard the ECHO (see _writingSource): our synchronous source write must not schedule a source->target push back.
      var written = TryCoerce(value, sourceType, out var fitted, FormatCulture) ? fitted : value;

      _sourceSpoke = false;
      _writingSource = true;
      try
      {
         WriteSource(written);
      }
      finally
      {
         _writingSource = false;
      }

      // Sync the Binding slot to what was written, since the echo guard skips that refresh; otherwise a later
      // re-coercion resurrects a pre-clamp value.
      if (Mode != BindingMode.TwoWay || _syncingSlot)
         return;

      _syncingSlot = true;
      try
      {
         // If the source raised a change and holds something else (clamped, or reset), take its answer; a source that
         // silently ignored the write is not re-read.
         if (_sourceSpoke && !Equals(ReadSource(), written))
         {
            UpdateTarget();
         }
         else
         {
            Target.SetValue(TargetProperty, targetValue, Priority);
         }
      }
      finally
      {
         _syncingSlot = false;
      }
   }

   private bool _syncingSlot;   // see UpdateSource: the slot refresh must not drive another write-back

}
