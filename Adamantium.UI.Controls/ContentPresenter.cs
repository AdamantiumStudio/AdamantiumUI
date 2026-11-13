using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

public class ContentPresenter : InputUIComponent
{
    private IUIComponent _currentRoot;
    private TextBlock _label;
    private TemplateResult _currentTemplateResult;
    private DataTemplate _currentTemplate;   // so a recycled container can reuse the visual
    // The key the current visual waits under in ParkedVisuals while it is away.
    private object _builtFor;

    // The templated parent, not the presenter: a theme swap rebuilds every template part, and a view filed under the old
    // presenter was never found again and stayed parked for good.
    private object ParkOwner => TemplatedParent ?? (object)this;

    // Its name in the templated parent's template: the same in the next template, and another part's - a header showing
    // the same content - is another.
    private string _part;

    private string Part => _part ??= (TemplatedParent as TemplatedUIComponent)?.PartName(this);

    private object _outgoingParkedKey;

    private IUIComponent _outgoingRoot;
    private TemplateResult _outgoingTemplateResult;
    private DataTemplate _outgoingTemplate;
    private bool _outgoingIsContent;
    private bool _isContentChanged;
    private bool _transitionPending;
    private bool _transitionRunning;
    private Action _afterTransition;
    private bool _lastContentRebuilt;
    private Size<double> _lastArrangeSize = new(double.NaN, double.NaN);
    // Supersedes a background build whose content has already been replaced.
    private int _deferToken;
    // Builds up to this token were for a template taken down: what they make is never parked.
    private int _abandonedThrough;
    private bool _deferInFlight;

    // One background build at a time, process-wide: overlapping builds see a circular dependency that does not exist.
    private static readonly System.Threading.SemaphoreSlim BuildQueue = new(1, 1);

    private Size<double> _lastMeasuredInner;   // without this element's margin/transform
    private Size<double> _lastContentDesired;
    private readonly Dictionary<FundamentalUIComponent, FundamentalUIComponent> _handedViews = new();

    public static readonly AdamantiumProperty ContentProperty = AdamantiumProperty.Register(nameof(Content),
        typeof(object), typeof(ContentPresenter), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, OnContentPropertyChanged));

    // AffectsMeasure: the visual is rebuilt inside MeasureOverride, so a template swap must invalidate it.
    public static readonly AdamantiumProperty ContentTemplateProperty = AdamantiumProperty.Register(nameof(ContentTemplate),
        typeof(DataTemplate), typeof(ContentPresenter), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, OnContentTemplateChanged));

    public static readonly AdamantiumProperty ContentTemplateSelectorProperty = AdamantiumProperty.Register(nameof(ContentTemplateSelector),
        typeof(DataTemplateSelector), typeof(ContentPresenter), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, OnContentTemplateSelectorChanged));

    /// <summary>Builds the content off the loop thread, showing <see cref="LoadingTemplate"/> meanwhile. For content that
    /// takes hundreds of milliseconds to build, not for list items.</summary>
    public static readonly AdamantiumProperty DeferContentProperty = AdamantiumProperty.Register(nameof(DeferContent),
        typeof(Boolean), typeof(ContentPresenter), new PropertyMetadata(false));

    /// <summary>What stands in the content's place while it is being built. Optional.</summary>
    public static readonly AdamantiumProperty LoadingTemplateProperty = AdamantiumProperty.Register(nameof(LoadingTemplate),
        typeof(DataTemplate), typeof(ContentPresenter), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty ContentTransitionProperty = AdamantiumProperty.Register(nameof(ContentTransition),
        typeof(ContentTransition), typeof(ContentPresenter), new PropertyMetadata(ContentTransition.None));

    public static readonly AdamantiumProperty TransitionDurationProperty = AdamantiumProperty.Register(nameof(TransitionDuration),
        typeof(Double), typeof(ContentPresenter), new PropertyMetadata(0.25));

    /// <summary>How the label generated for string content is cut when it does not fit. Default None: the label reports
    /// the width it wants.</summary>
    public static readonly AdamantiumProperty TextTrimmingProperty = AdamantiumProperty.Register(nameof(TextTrimming),
        typeof(TextTrimming), typeof(ContentPresenter),
        new PropertyMetadata(TextTrimming.None, PropertyMetadataOptions.AffectsMeasure));

    public TextTrimming TextTrimming
    {
        get => GetValue<TextTrimming>(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    /// <summary>How the label generated for string content wraps. Default NoWrap: one line.</summary>
    public static readonly AdamantiumProperty TextWrappingProperty = AdamantiumProperty.Register(nameof(TextWrapping),
        typeof(TextWrapping), typeof(ContentPresenter),
        new PropertyMetadata(TextWrapping.NoWrap, PropertyMetadataOptions.AffectsMeasure, OnLinesChanged));

    public TextWrapping TextWrapping
    {
        get => GetValue<TextWrapping>(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>The most lines of the label generated for string content (<see cref="TextBlock.MaxLines"/>); 0, the
    /// default, for any number.</summary>
    public static readonly AdamantiumProperty MaxLinesProperty = AdamantiumProperty.Register(nameof(MaxLines),
        typeof(int), typeof(ContentPresenter),
        new PropertyMetadata(0, PropertyMetadataOptions.AffectsMeasure, OnLinesChanged));

    public int MaxLines
    {
        get => GetValue<int>(MaxLinesProperty);
        set => SetValue(MaxLinesProperty, value);
    }

    /// <summary>Whether the label generated for string content balances its lines (<see cref="TextBlock.BalanceLines"/>).</summary>
    public static readonly AdamantiumProperty BalanceLinesProperty = AdamantiumProperty.Register(nameof(BalanceLines),
        typeof(bool), typeof(ContentPresenter),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsMeasure, OnLinesChanged));

    public bool BalanceLines
    {
        get => GetValue<bool>(BalanceLinesProperty);
        set => SetValue(BalanceLinesProperty, value);
    }

    static ContentPresenter()
    {
        ForegroundProperty.OverrideMetadata(typeof(ContentPresenter),
            new PropertyMetadata(null, PropertyMetadataOptions.Inherits | PropertyMetadataOptions.AffectsRender));
        FontSizeProperty.OverrideMetadata(typeof(ContentPresenter),
            new PropertyMetadata(14.0, PropertyMetadataOptions.Inherits | PropertyMetadataOptions.AffectsMeasure));
    }

    private static void OnContentPropertyChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is ContentPresenter presenter)
        {
            presenter.OnContentChangedInternal(e.OldValue, e.NewValue);
        }
    }

    private void OnContentChangedInternal(object oldContent, object newContent)
    {
        _isContentChanged = true;

        // Data content becomes the DataContext of the visual built for it (SetContentContext), never of the presenter:
        // the presenter's own bindings resolve against its DataContext, and a local value there would mask inheritance.
    }

    private void SetContentContext(object content)
    {
        if (_currentTemplateResult != null && _currentRoot is FundamentalUIComponent root) root.DataContext = content;
    }

    // True when a new visual was built and must be measured; false on reuse or no change.
    private bool UpdateVisualContent(object newContent)
    {

        if (!_isContentChanged)
            return false;

        _isContentChanged = false;

        // Same template as the current visual (a recycled container): keep the visual and its GPU buffers - rebuilding
        // per scroll frame ran the device out of memory. Not a visual kept between visits (x:KeepAlive): it belongs to
        // the content it was built for, which comes back to it, and handed another it would show that one in its place.
        // Only where an owner of items lets it go with its item; anywhere else nothing would ever let it go.
        if (newContent != null && newContent is not IUIComponent && _currentRoot != null && _currentTemplate != null
            && !_deferInFlight && !(TemplatedParent is ItemsControl && ParkedVisuals.ShouldKeep(_currentRoot)))
        {
            var reuseTemplate = ContentTemplate ?? ContentTemplateSelector?.SelectTemplate(newContent, this);
            if (ReferenceEquals(reuseTemplate, _currentTemplate))
            {
                SetContentContext(newContent);
                return false;
            }
        }

        // The same for plain string content in the generated TextBlock.
        if (newContent != null && newContent is not IUIComponent && _currentTemplate == null
            && _currentRoot is TextBlock reusableText && ReferenceEquals(reusableText, _label)
            && ContentTemplate == null && ContentTemplateSelector == null)
        {
            reusableText.LetSelectionGo();
            reusableText.Text = newContent.ToString();
            return false;
        }

        if (_outgoingRoot != null)
            RemoveOutgoing();

        // Never in the designer: there a swap is only a reload, and the slide left the captured frame blank.
        var animate = ContentTransition != ContentTransition.None && newContent != null
                      && !Design.IsDesignMode;

        // Decided here, while the content it was built for is still known.
        var parkedKey = _currentRoot != null && ParkedVisuals.ShouldKeep(_currentRoot) && CanComeBack(_builtFor)
            ? _builtFor
            : null;

        if (animate)
        {
            _outgoingRoot = _currentRoot;
            _outgoingTemplateResult = _currentTemplateResult;
            _outgoingTemplate = _currentTemplate;
            _outgoingParkedKey = parkedKey;
            _outgoingIsContent = ReferenceEquals(_currentRoot, _builtFor);
        }
        else if (_currentRoot != null)
        {
            if (parkedKey != null) ParkCurrent(parkedKey, _currentRoot, _currentTemplateResult, _currentTemplate);
            else Release(_currentRoot, _currentTemplateResult);
        }

        // A background build still running was for content that is no longer ours.
        _deferToken++;
        _deferInFlight = false;

        _currentRoot = null;
        _currentTemplateResult = null;
        _currentTemplate = null;
        _builtFor = null;
        _label = null;

        if (newContent != null)
            BuildCurrent(newContent);

        // The slide distance is the laid-out size, so it starts at the next arrange.
        _transitionPending = animate && (_currentRoot != null || _outgoingRoot != null);
        if (_transitionPending) ContentTransitions.Prepare(_currentRoot, _outgoingRoot);
        if (animate && !_transitionPending)
            RemoveOutgoing();

        return true;
    }

    private void BuildCurrent(object newContent)
    {
        if (ParkedVisuals.TryTake(ParkOwner, newContent, Part, this, out var parkedRoot, out var parkedBuilt,
                out var parkedTemplate, out var parkedHostSize))
        {
            ShowParked(newContent, parkedRoot, parkedBuilt, parkedTemplate, parkedHostSize);
            return;
        }

        _builtFor = newContent;

        if (newContent is IUIComponent iuiComponent)
        {
            if (iuiComponent.IsParked) ParkedSubtree.Revalidate(iuiComponent);
            _currentRoot = iuiComponent;
        }
        else
        {
            var dataTemplate = ContentTemplate ?? ContentTemplateSelector?.SelectTemplate(newContent, this);

            if (dataTemplate != null && WantsDeferredBuild)
            {
                _currentTemplate = dataTemplate;
                StartDeferredBuild(newContent, dataTemplate);
                return;
            }

            if (dataTemplate != null)
            {
                BuildFromTemplate(newContent, dataTemplate);
                return;
            }
            else
            {
                var textBlock = new TextBlock
                {
                    Text = newContent.ToString(),
                    HorizontalTextAlignment = ToTextAlignment(HorizontalAlignment),
                    VerticalTextAlignment = ToTextAlignment(VerticalAlignment),
                    // Centered, not stretched: a stretched block does not hand its height to the text layout, so the
                    // label sat against the top of its slot.
                    HorizontalAlignment = HorizontalAlignment,
                    VerticalAlignment = VerticalAlignment == VerticalAlignment.Stretch
                        ? VerticalAlignment.Center
                        : VerticalAlignment
                };
                // Bound, not copied: an inherited change can step over the label without notifying it. A presenter with
                // no color of its own says nothing - handed its null, the label threw on every frame it drew.
                textBlock.SetBinding(nameof(TextBlock.Foreground),
                    new Core.Data.Binding(nameof(Foreground)) { Source = this, TargetNullValue = AdamantiumProperty.UnsetValue });

                textBlock.SetBinding(nameof(TextBlock.FontSize),
                    new Core.Data.Binding(nameof(FontSize)) { Source = this });

                textBlock.SetBinding(nameof(TextBlock.TextTrimming),
                    new Core.Data.Binding(nameof(TextTrimming)) { Source = this });

                _label = textBlock;
                PassLinesTo(textBlock);
                _currentRoot = textBlock;
            }
        }

        if (_currentRoot != null)
        {
            Host(_currentRoot);
            SetContentContext(newContent);
        }
    }

    private void ShowParked(object content, IUIComponent root, TemplateResult built, DataTemplate template,
        Size<double> hostSize)
    {
        // Another window or theme: the attach below must revalidate every node the ordinary way.
        if (!ParkedVisuals.IsUnchanged)
        {
            ParkedSubtree.Revalidate(root);
        }

        if (built?.HostComponent is { } builtBy && !ReferenceEquals(builtBy, this))
        {
            built.Rehost(this);
            ParkedVisuals.MoveOwner(builtBy, this);
        }

        _currentRoot = root;
        _currentTemplateResult = built;
        _currentTemplate = template;
        _builtFor = content;
        if (template == null && content is not IUIComponent && root is TextBlock parkedLabel)
        {
            _label = parkedLabel;
            PassLinesTo(parkedLabel);
        }

        Host(_currentRoot);
        // Re-measured only in a host of another size: the layout it kept is still right otherwise.
        ParkedSubtree.Unpark(_currentRoot, remeasure: hostSize != _lastArrangeSize);

        // The re-theming walk missed it while parked. After the attach: a node out of the tree has no queue to register
        // the restyle with.
        (_currentRoot as FundamentalUIComponent)?.RestyleIfItMissedASwap();

        SetContentContext(content);
    }

    private void BuildFromTemplate(object content, DataTemplate template)
    {
        _currentTemplateResult = template.Build(this);
        _currentRoot = _currentTemplateResult?.RootComponent;
        _currentTemplate = template;

        if (_currentRoot == null) return;

        SetContentContext(content);
        Host(_currentRoot);
        InvalidateMeasure();
    }

    private void ShowLoadingVisual(object content)
    {
        if (!_deferInFlight || _currentRoot != null) return;

        var loading = LoadingTemplate?.Build(this);
        if (loading?.RootComponent == null) return;

        _currentTemplateResult = loading;
        _currentRoot = loading.RootComponent;
        SetContentContext(content);
        Host(_currentRoot);
        InvalidateMeasure();
    }

    private void DropCurrent()
    {
        if (_currentRoot == null) return;

        Release(_currentRoot, _currentTemplateResult);
        _currentRoot = null;
        _currentTemplateResult = null;
    }

    // A surface that renders once has no next frame for the content to arrive in.
    private bool WantsDeferredBuild =>
        DeferContent && !Design.IsDesignMode && (RootVisual as IRootVisualComponent)?.RendersOnce != true;

    private void StartDeferredBuild(object content, DataTemplate template)
    {
        var token = ++_deferToken;
        var owner = ParkOwner;
        _deferInFlight = true;

        // The spinner belongs to the tab being entered: it appears after the swap, if the content has not arrived by then.
        if (_outgoingRoot == null) ShowLoadingVisual(content);
        else _afterTransition = () => ShowLoadingVisual(content);

        Task.Run(async () =>
        {
            TemplateResult built = null;
            Exception failure = null;

            await BuildQueue.WaitAsync().ConfigureAwait(false);
            try
            {
                // A tab already left is skipped, so the one being waited for starts now.
                if (token != System.Threading.Volatile.Read(ref _deferToken))
                {
                    return;
                }

                try
                {
                    built = template.Build(this);
                }
                catch (Exception e)
                {
                    failure = e;
                }

                // Posted before the next build may start: one left behind - its tab left and entered again - is adopted
                // (parked) before the next build for that content is, which then takes the parked one up instead.
                LoopSignal.Post(() => AdoptDeferred(token, owner, content, template, built, failure));
            }
            finally
            {
                BuildQueue.Release();
            }
        });
    }

    private void AdoptDeferred(int token, object owner, object content, DataTemplate template, TemplateResult built,
        Exception failure)
    {
        if (failure != null)
        {
            Console.WriteLine(failure);
        }

        if (token != _deferToken)
        {
            // Left before it was ready: a view that asked to be kept is parked like one that was on screen - unless its
            // template went (the next one builds its own), or a view for that content is already kept or shown, which
            // it must not replace.
            if (built?.RootComponent is { } finished && ParkedVisuals.ShouldKeep(finished) && token > _abandonedThrough &&
                owner is not FundamentalUIComponent { IsDiscarded: true } && OwnerHolds(owner, content) &&
                !ParkedVisuals.Holds(owner, content, Part) && !Shows(content))
            {
                ParkedVisuals.Keep(owner, content, finished, built, template, new Size<double>(Double.NaN, Double.NaN),
                    Part);
                return;
            }

            DiscardUnshown(built);
            return;
        }

        // Waits for its own swap, or the outgoing tab slides over it.
        if (_transitionPending || _transitionRunning)
        {
            _afterTransition = () => AdoptDeferred(token, owner, content, template, built, failure);
            return;
        }
        if (_currentRoot == null && built?.RootComponent != null)
        {
            ShowLoadingVisual(content);
            if (_currentRoot != null)
            {
                var shownAt = AnimationManager.Ticks;
                var recordedAt = Compositor.Recorded;
                AnimationManager.AddTicker(_ =>
                {
                    if (!Compositor.RecordedSince(recordedAt, shownAt)) return false;
                    AdoptDeferred(token, owner, content, template, built, failure);
                    return true;
                });
                return;
            }
        }

        _deferInFlight = false;

        // Built while an earlier build for the same content was finishing - its tab left and entered again - and parked
        // when it landed: that one is shown, not a second copy.
        if (ParkedVisuals.TryTake(owner, content, Part, this, out var waiting, out var waitingBuilt,
                out var waitingTemplate, out var waitingHostSize))
        {
            DiscardUnshown(built);
            DropCurrent();
            ShowParked(content, waiting, waitingBuilt, waitingTemplate, waitingHostSize);
            InvalidateMeasure();
            return;
        }

        if (built?.RootComponent == null)
        {
            // Nothing came back: build it here, so a broken view throws where it always threw.
            DropCurrent();
            BuildFromTemplate(content, template);
            return;
        }

        DropCurrent();

        _currentTemplateResult = built;
        _currentRoot = built.RootComponent;
        SetContentContext(content);
        Host(_currentRoot);
        InvalidateMeasure();
    }

    // Shown here now, built for that content: not the loading visual of a build still under way.
    private bool Shows(object content) => _currentRoot != null && !_deferInFlight && Equals(_builtFor, content);

    // Never in the tree, so nothing else announces its discard: its own release (a browser's) would never run. A view a
    // template hands out again and again - one instance for all - may be shown or parked by now, and is left alone.
    private static void DiscardUnshown(TemplateResult built)
    {
        if (built == null)
        {
            return;
        }

        if (built.RootComponent is { VisualParent: not null } or { IsParked: true })
        {
            return;
        }

        if (built.RootComponent != null)
        {
            ParkedSubtree.Discard(built.RootComponent);
        }

        built.Destroy();
    }

    private void RemoveOutgoing()
    {
        if (_outgoingRoot == null)
            return;

        // Asked again: the content may have left its owner while the page slid out - a tab closed mid-transition.
        if (_outgoingParkedKey != null && CanComeBack(_outgoingParkedKey))
            ParkCurrent(_outgoingParkedKey, _outgoingRoot, _outgoingTemplateResult, _outgoingTemplate);
        else Release(_outgoingRoot, _outgoingTemplateResult);

        _outgoingRoot = null;
        _outgoingTemplateResult = null;
        _outgoingTemplate = null;
        _outgoingParkedKey = null;
    }

    private bool IsVisualChild(IUIComponent visual)
    {
        foreach (var child in VisualChildren)
        {
            if (ReferenceEquals(child, visual)) return true;
        }
        return false;
    }

    /// <summary>Parks the content it shows when that is kept between visits (<c>x:KeepAlive</c>), for the next template's
    /// presenter to take up. A control whose template holds this presenter calls it from its <c>OnTemplateRemoving</c>
    /// when another template follows. A slide or a background build under way ends here.</summary>
    public void ParkShown()
    {
        _abandonedThrough = _deferToken;
        _deferToken++;
        _deferInFlight = false;
        _transitionPending = false;
        TransitionFinished();

        // An owner thrown away with the template around it - a part of another control's template - takes no one back.
        if (_currentRoot == null || !ParkedVisuals.ShouldKeep(_currentRoot) || !CanComeBack(_builtFor) ||
            ParkOwner is FundamentalUIComponent { IsDiscarded: true })
        {
            return;
        }

        ParkCurrent(_builtFor, _currentRoot, _currentTemplateResult, _currentTemplate);
        _currentRoot = null;
        _currentTemplateResult = null;
        _currentTemplate = null;
        _builtFor = null;
        _label = null;
    }

    private void ParkCurrent(object key, IUIComponent root, TemplateResult built, DataTemplate template)
    {
        ParkedVisuals.Keep(ParkOwner, key, root, built, template, _lastArrangeSize, Part);
        RemoveVisualChild(root);
        Unhost(root);
    }

    internal bool ShowsHandedContent =>
        _currentRoot != null && ReferenceEquals(_currentRoot, _builtFor) &&
        GetValueSource(ContentProperty) is ValuePriority.Binding or ValuePriority.Template;

    // LENT, not ours: an items control's own item goes back to it, and a tip element goes back to the element that set
    // it - the shared card shows one after another, and discarding the last one shown broke it for good. What this
    // presenter built for a tip, the label for a string, is its own all the same.
    private bool IsOwnersOwnItem(IUIComponent visual) =>
        visual != null && (TemplatedParent is ItemsControl owner && owner.Items.Contains(visual) ||
                           TemplatedParent is ToolTip && IsGivenContent(visual) ||
                           ContentControl.IsLentContent(visual));

    private bool IsGivenContent(IUIComponent visual) =>
        ReferenceEquals(visual, _builtFor) || ReferenceEquals(visual, _outgoingRoot) && _outgoingIsContent;

    private bool CanComeBack(object content) => OwnerHolds(TemplatedParent, content);

    private static bool OwnerHolds(object owner, object content) =>
        owner is not ItemsControl items || items.HoldsContent(content);

    private void Release(IUIComponent visual, TemplateResult built)
    {
        // Our own children, not the visual's parent pointer: adoption does not empty this collection, and an element
        // another presenter adopted must keep its new home.
        if (built == null && !IsVisualChild(visual)) return;

        // Content authored in markup is not a template part, so nothing else announces its discard. Not an item of the
        // owner, though: the owner hands it back, and a discard is one way.
        if (!IsOwnersOwnItem(visual))
        {
            if (_discardBuf.Count > 0) _discardBuf.Clear();
            CollectForDiscard(visual);
            Core.DiscardedVisuals.Publish(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_discardBuf));
            _discardBuf.Clear();
        }

        RemoveVisualChild(visual);
        Unhost(visual);
        built?.Destroy();
    }

    private void Host(IUIComponent root)
    {
        AddVisualChild(root);
        (TemplatedParent as ItemsControl)?.HoldShownItem(root);
        if (root is FundamentalUIComponent { LogicalParent: { } holder } given && !ReferenceEquals(holder, this))
        {
            given.ShowIn(this);
        }
        else if (!ReferenceEquals(root, _builtFor) && ViewHolder is { } owner && root is FundamentalUIComponent built)
        {
            owner.AddLogicalChild(built);
            built.ShowIn(this);
            _handedViews[built] = owner;
        }
        else
        {
            AddLogicalChild(root);
        }
    }

    private FundamentalUIComponent ViewHolder =>
        IsGivenByTemplate(ContentProperty) ? TemplatedParent as FundamentalUIComponent : null;

    private void Unhost(IUIComponent root)
    {
        if (ReferenceEquals(root.LogicalParent, this))
        {
            RemoveLogicalChild(root);
        }
        else
        {
            TakeBackHandedView(root);
        }
    }

    private void TakeBackHandedView(IUIComponent root)
    {
        if (root is FundamentalUIComponent built && _handedViews.Remove(built, out var owner)
            && ReferenceEquals(built.LogicalParent, owner))
        {
            owner.RemoveLogicalChild(built);
        }
    }

    /// <summary>Destroyed: lets the content go, since a root held only by these fields cannot be reached otherwise.</summary>
    protected override void OnDiscarded()
    {
        base.OnDiscarded();

        // A build still under way lands on no one: what it makes is let go, not hosted here or parked.
        _abandonedThrough = _deferToken;
        _deferToken++;
        _deferInFlight = false;
        DropContentHandles();
    }

    private void DropContentHandles()
    {
        DropCurrentHandles();
        DropOutgoingHandles();
    }

    // Removed, not just forgotten: a root discarded while this presenter lives is still our child and would stay drawn.
    private void DropCurrentHandles()
    {
        DiscardBuilt(_currentRoot, !ReferenceEquals(_currentRoot, _builtFor));
        LetGo(_currentRoot);

        _currentRoot = null;
        _currentTemplateResult = null;
        _currentTemplate = null;
    }

    private void DropOutgoingHandles()
    {
        DiscardBuilt(_outgoingRoot, !_outgoingIsContent);
        LetGo(_outgoingRoot);

        _outgoingRoot = null;
        _outgoingTemplateResult = null;
        _outgoingTemplate = null;
    }

    private void DiscardBuilt(IUIComponent root, bool built)
    {
        if (root == null || !built)
        {
            return;
        }

        if (_discardBuf.Count > 0)
        {
            _discardBuf.Clear();
        }

        CollectForDiscard(root);
        Core.DiscardedVisuals.Publish(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_discardBuf));
        _discardBuf.Clear();
    }

    private void LetGo(IUIComponent root)
    {
        if (root == null || !IsVisualChild(root)) return;
        RemoveVisualChild(root);
        Unhost(root);
    }

    /// <summary>Lets go of its content once that is released while this presenter lives on: the fields naming the root
    /// would otherwise be the last thing holding it.</summary>
    protected internal override void OnVisualChildReleased(Core.FundamentalUIComponent child)
    {
        base.OnVisualChildReleased(child);

        // Only the side that went: the view sliding out released must not take the one sliding in with it.
        if (ReferenceEquals(child, _currentRoot))
        {
            DropCurrentHandles();
        }

        if (ReferenceEquals(child, _outgoingRoot))
        {
            DropOutgoingHandles();
        }
    }

    /// <summary>Also forgets a root another parent has taken, so the fields do not go on naming a control that has moved
    /// on - and keep it alive after its new home lets it go. Held here only for want of a holder, or a view built here
    /// and handed to its control, it is let go too.</summary>
    protected internal override void DisownVisualChild(IUIComponent child)
    {
        Unhost(child);

        if (ReferenceEquals(child, _currentRoot))
        {
            _currentRoot = null;
            _currentTemplateResult = null;
            _currentTemplate = null;
            _builtFor = null;
        }

        if (ReferenceEquals(child, _outgoingRoot))
        {
            _outgoingRoot = null;
            _outgoingTemplateResult = null;
            _outgoingTemplate = null;
            _outgoingParkedKey = null;
        }

        base.DisownVisualChild(child);
    }

    private readonly List<IFundamentalUIComponent> _discardBuf = new();

    private void CollectForDiscard(IUIComponent node)
    {
        if (node is IFundamentalUIComponent fundamental) _discardBuf.Add(fundamental);
        foreach (var child in node.VisualChildren) CollectForDiscard(child);
    }

    private void StartTransition(Size<double> size)
    {
        ClipToBounds = true;
        _transitionRunning = true;
        ContentTransitions.Run(ContentTransition, TransitionDuration, size, _currentRoot, _outgoingRoot, TransitionFinished);
    }

    private void TransitionFinished()
    {
        _transitionRunning = false;
        if (_outgoingRoot != null)
        {
            RemoveOutgoing();
            _lastContentDesired = default;
            InvalidateMeasure();
        }

        var waiting = _afterTransition;
        _afterTransition = null;
        waiting?.Invoke();
    }

    private static void OnLinesChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is ContentPresenter presenter && presenter._label != null
                                            && ReferenceEquals(presenter._currentRoot, presenter._label))
        {
            presenter.PassLinesTo(presenter._label);
        }
    }

    private void PassLinesTo(TextBlock label)
    {
        Pass(TextBlock.TextWrappingProperty, TextWrapping != TextWrapping.NoWrap, TextWrapping);
        Pass(TextBlock.MaxLinesProperty, MaxLines != 0, MaxLines);
        Pass(TextBlock.BalanceLinesProperty, BalanceLines, BalanceLines);
        label.InvalidateMeasure();

        void Pass(AdamantiumProperty labels, bool set, object value)
        {
            if (set)
            {
                label.SetValue(labels, value);
            }
            else if (label.GetValueSource(labels) == ValuePriority.Local)
            {
                label.ClearValue(labels);
            }
        }
    }

    private static void OnContentTemplateChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is ContentPresenter presenter)
        {
            presenter.OnContentChangedInternal(e.OldValue, e.NewValue);
        }
    }

    private static void OnContentTemplateSelectorChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is ContentPresenter presenter)
        {
            presenter.OnContentChangedInternal(e.OldValue, e.NewValue);
        }
    }

    [Content]
    public object Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public DataTemplate ContentTemplate
    {
        get => GetValue<DataTemplate>(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    public DataTemplateSelector ContentTemplateSelector
    {
        get => GetValue<DataTemplateSelector>(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    public bool DeferContent
    {
        get => GetValue<bool>(DeferContentProperty);
        set => SetValue(DeferContentProperty, value);
    }

    public DataTemplate LoadingTemplate
    {
        get => GetValue<DataTemplate>(LoadingTemplateProperty);
        set => SetValue(LoadingTemplateProperty, value);
    }

    public ContentTransition ContentTransition
    {
        get => GetValue<ContentTransition>(ContentTransitionProperty);
        set => SetValue(ContentTransitionProperty, value);
    }

    public Double TransitionDuration
    {
        get => GetValue<Double>(TransitionDurationProperty);
        set => SetValue(TransitionDurationProperty, value);
    }

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        _lastContentRebuilt = UpdateVisualContent(Content);

        var sizeBefore = DesiredSize;
        // Reused data-only content keeps its size. The inner size is returned, not DesiredSize, which would add the
        // margin again on every skip; and only while the child is still the size the cache was made from.
        if (!_lastContentRebuilt && _currentRoot is IMeasurableComponent { IsMeasureValid: true } measured
            && PreviousMeasureConstraint == availableSize
            && LayoutTransform == null
            && measured.DesiredSize == _lastContentDesired)
        {
            return _lastMeasuredInner;
        }

        var size = base.MeasureOverride(availableSize);
        _lastMeasuredInner = size;
        _lastContentDesired = _currentRoot is IMeasurableComponent content ? content.DesiredSize : default;

        // A rebuild inside a directly requested measure leaves the parent with a size of content that no longer exists.
        if (_lastContentRebuilt)
        {
            (VisualParent as IMeasurableComponent)?.InvalidateMeasure();
        }

        return size;
    }

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        // A pending transition is not "nothing changed": a kept view comes back arrange-valid at the same size, and
        // skipping swallowed its entrance. Nor is a root arranged into another slot - one adopted after a deferred build
        // was arranged on its own, into its desired size.
        if (!_transitionPending && !_lastContentRebuilt
            && _currentRoot is IMeasurableComponent { IsArrangeValid: true } root && finalSize == _lastArrangeSize
            && root.PreviousArrangeSlot == new Rect(finalSize))
            return finalSize;

        _lastArrangeSize = finalSize;

        var size = base.ArrangeOverride(finalSize);

        if (_transitionPending)
        {
            _transitionPending = false;
            StartTransition(finalSize);
        }

        return size;
    }

    private static HorizontalTextAlignment ToTextAlignment(HorizontalAlignment alignment) => alignment switch
    {
        HorizontalAlignment.Center => HorizontalTextAlignment.Center,
        HorizontalAlignment.Right => HorizontalTextAlignment.Right,
        _ => HorizontalTextAlignment.Left,
    };

    private static VerticalTextAlignment ToTextAlignment(VerticalAlignment alignment) => alignment switch
    {
        VerticalAlignment.Top => VerticalTextAlignment.Top,
        VerticalAlignment.Bottom => VerticalTextAlignment.Bottom,
        _ => VerticalTextAlignment.Center,
    };
}
