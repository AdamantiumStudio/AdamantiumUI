using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;

namespace Adamantium.Navigation;

/// <summary>Default region: owns the lifecycle sequence, the journal and the active-view-model set, resolving targets via
/// the DI container. UI-free - an adapter (UI layer) observes CurrentViewModel/ActiveViewModels and drives a control.</summary>
public sealed class Region : PropertyChangedBase, IRegion
{
    private static readonly AsyncLocal<ImmutableStack<(Region Region, Task Settled)>> Within = new();

    private readonly IDependencyResolver _resolver;
    private readonly List<object> _activeViewModels = [];
    private object _currentViewModel;
    private string _currentViewKey;
    private CancellationTokenSource _pending;
    private Task _settled = Task.CompletedTask;

    public Region(string name, IDependencyResolver resolver, INavigationService navigationService)
    {
        Name = name;
        _resolver = resolver;
        NavigationService = navigationService;
        Journal = new NavigationJournal();
    }

    // The façade a NavigationContext hands to lifecycle callbacks; set by the manager when the service is available.
    internal INavigationService NavigationService { get; set; }

    public string Name { get; }
    public INavigationJournal Journal { get; }
    public IReadOnlyList<object> ActiveViewModels => _activeViewModels;
    public bool SingleActiveView { get; set; }

    public object CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public string CurrentViewKey
    {
        get => _currentViewKey;
        private set => SetProperty(ref _currentViewKey, value);
    }

    public bool CanGoBack => Journal.CanGoBack;
    public bool CanGoForward => Journal.CanGoForward;

    public event EventHandler<RegionNavigationEventArgs> Navigated;
    public event EventHandler ActiveViewsChanged;

    public Task<NavigationResult> NavigateToAsync<TViewModel>(NavigationParameters parameters = null, CancellationToken cancellationToken = default)
        => NavigateToAsync(typeof(TViewModel), parameters, cancellationToken);

    public Task<NavigationResult> NavigateToViewAsync<TViewModel>(string viewKey, NavigationParameters parameters = null, CancellationToken cancellationToken = default)
        => NavigateToViewAsync(typeof(TViewModel), viewKey, parameters, cancellationToken);

    public Task<NavigationResult> NavigateToAsync(Type viewModelType, NavigationParameters parameters = null, CancellationToken cancellationToken = default)
        => NavigateToViewAsync(viewModelType, null, parameters, cancellationToken);

    public Task<NavigationResult> NavigateToInstanceAsync(object viewModel, string viewKey, NavigationParameters parameters = null, CancellationToken cancellationToken = default)
        => NavigateCoreAsync(viewModel?.GetType(), viewKey, viewModel, parameters, cancellationToken);

    public Task<NavigationResult> NavigateToViewAsync(Type viewModelType, string viewKey, NavigationParameters parameters = null, CancellationToken cancellationToken = default)
        => NavigateCoreAsync(viewModelType, viewKey, null, parameters, cancellationToken);

    private Task<NavigationResult> NavigateCoreAsync(Type viewModelType, string viewKey, object instance, NavigationParameters parameters, CancellationToken cancellationToken)
        => RunAsync(cancellationToken, async token =>
        {
            var context = new NavigationContext(this, NavigationService, viewModelType, _currentViewModel, parameters, NavigationMode.New, token);
            if (!await ConfirmLeaveAsync(context))
            {
                return NavigationResult.Vetoed();
            }

            // A given instance is the target, full stop - the container is not asked at all.
            var target = instance ?? FindReusable(viewModelType, context) ?? _resolver.Resolve(viewModelType);
            await EnterAsync(context, target);

            Journal.RecordNavigation(new NavigationJournalEntry(viewModelType, target, context.Parameters, viewKey));
            // The key BEFORE the model: when a view-model is read through several views the model does not change, so
            // this is the only property that moves, and an adapter must not see it arrive after the view-model settled.
            CurrentViewKey = viewKey;
            SetActive(target);
            return Settle(context);
        });

    public Task<NavigationResult> GoBackAsync(CancellationToken cancellationToken = default) => GoAsync(NavigationMode.Back, cancellationToken);
    public Task<NavigationResult> GoForwardAsync(CancellationToken cancellationToken = default) => GoAsync(NavigationMode.Forward, cancellationToken);

    private Task<NavigationResult> GoAsync(NavigationMode mode, CancellationToken cancellationToken)
        => RunAsync(cancellationToken, async token =>
        {
            var entry = mode == NavigationMode.Back
                ? (Journal.CanGoBack ? Journal.BackStack[^1] : null)
                : (Journal.CanGoForward ? Journal.ForwardStack[^1] : null);
            if (entry == null)
            {
                return NavigationResult.Vetoed();
            }

            var context = new NavigationContext(this, NavigationService, entry.ViewModelType, _currentViewModel, entry.Parameters, mode, token);
            if (!await ConfirmLeaveAsync(context))
            {
                return NavigationResult.Vetoed();
            }

            var target = entry.ViewModel ?? _resolver.Resolve(entry.ViewModelType);
            await EnterAsync(context, target);

            if (mode == NavigationMode.Back)
            {
                Journal.Back();
            }
            else
            {
                Journal.Forward();
            }
            CurrentViewKey = entry.ViewKey;
            SetActive(target);
            return Settle(context);
        });

    private async Task<NavigationResult> RunAsync(CancellationToken cancellationToken, Func<CancellationToken, Task<NavigationResult>> navigate)
    {
        var within = Within.Value ?? ImmutableStack<(Region Region, Task Settled)>.Empty;
        var fromOwnHook = within.Any(w => ReferenceEquals(w.Region, this) && !w.Settled.IsCompleted);

        _pending?.Cancel();
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pending = pending;
        var previous = _settled;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _settled = settled.Task;
        Within.Value = within.Push((this, settled.Task));
        try
        {
            if (!fromOwnHook)
            {
                await previous;
            }
            pending.Token.ThrowIfCancellationRequested();
            return await navigate(pending.Token);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
            return NavigationResult.Vetoed();
        }
        catch (Exception ex)
        {
            return NavigationResult.Failed(ex);
        }
        finally
        {
            if (ReferenceEquals(_pending, pending))
            {
                _pending = null;
            }
            settled.SetResult();
        }
    }

    private static async Task EnterAsync(NavigationContext context, object target)
    {
        context.TargetViewModel = target;
        if (target is INavigationAware entering)
        {
            await entering.OnNavigatedToAsync(context, context.CancellationToken);
        }
        context.CancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(context.SourceViewModel, target) && context.SourceViewModel is INavigationAware leaving)
        {
            await leaving.OnNavigatedFromAsync(context, context.CancellationToken);
        }
    }

    public void Add(object viewModel)
    {
        if (viewModel == null || _activeViewModels.Contains(viewModel)) return;
        _activeViewModels.Add(viewModel);
        RaiseActiveViewsChanged();
    }

    public void Remove(object viewModel)
    {
        if (viewModel == null || !_activeViewModels.Remove(viewModel)) return;
        if (ReferenceEquals(viewModel, _currentViewModel))
            CurrentViewModel = _activeViewModels.Count > 0 ? _activeViewModels[^1] : null;
        RaiseActiveViewsChanged();
    }

    public void Activate(object viewModel)
    {
        if (viewModel == null) return;
        Add(viewModel);
        CurrentViewModel = viewModel;
    }

    public void Deactivate(object viewModel)
    {
        if (ReferenceEquals(viewModel, _currentViewModel))
            CurrentViewModel = _activeViewModels.Count > 0 ? _activeViewModels[^1] : null;
    }

    // Reuse the current (or any active) instance of the same type when it says it can serve this navigation.
    private object FindReusable(Type viewModelType, NavigationContext context)
    {
        if (_currentViewModel != null && _currentViewModel.GetType() == viewModelType
            && _currentViewModel is INavigationAware currentAware && currentAware.IsNavigationTarget(context))
            return _currentViewModel;

        foreach (var vm in _activeViewModels)
            if (vm.GetType() == viewModelType && vm is INavigationAware aware && aware.IsNavigationTarget(context))
                return vm;

        return null;
    }

    private void SetActive(object target)
    {
        if (SingleActiveView && _currentViewModel != null && !ReferenceEquals(_currentViewModel, target)
            && _activeViewModels.Remove(_currentViewModel))
            RaiseActiveViewsChanged();

        if (!_activeViewModels.Contains(target))
        {
            _activeViewModels.Add(target);
            RaiseActiveViewsChanged();
        }
        CurrentViewModel = target;
    }

    private NavigationResult Settle(NavigationContext context)
    {
        RaisePropertyChanged(nameof(CanGoBack));
        RaisePropertyChanged(nameof(CanGoForward));
        var result = NavigationResult.Ok(context.TargetViewModel);
        Navigated?.Invoke(this, new RegionNavigationEventArgs(context, result));
        return result;
    }

    private static async Task<bool> ConfirmLeaveAsync(NavigationContext context)
    {
        if (context.SourceViewModel is IConfirmNavigation guard && !await guard.CanNavigateAwayAsync(context, context.CancellationToken))
        {
            return false;
        }
        context.CancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private void RaiseActiveViewsChanged() => ActiveViewsChanged?.Invoke(this, EventArgs.Empty);
}
