using System;
using System.ComponentModel;
using Adamantium.Core;

namespace Adamantium.Navigation;

/// <summary>
/// A view model shown as a pane of a docking region. Its id is its type's name, it opens in the document area and may be
/// moved anywhere; a derived class gives its title and overrides what differs.
/// </summary>
public abstract class DockablePaneViewModel : PropertyChangedBase, IDockablePane
{
    private readonly Func<string> _title;
    private DockZone _paneZone;

    /// <param name="title">The tab's label, read again whenever <paramref name="titleSource"/> changes.</param>
    /// <param name="titleSource">What the title is read from, if it changes - a language table, so the tab follows the
    /// application's language: <c>base(() => EditorStrings.Scene, EditorStrings.Current)</c>. Listened to weakly.</param>
    /// <param name="zone">Where the pane opens.</param>
    protected DockablePaneViewModel(Func<string> title, INotifyPropertyChanged titleSource = null,
        DockZone zone = DockZone.Center)
    {
        _title = title;
        _paneZone = zone;
        if (titleSource != null) TitleListener.Listen(this, titleSource);
    }

    /// <summary>The pane's identity in the layout: the type's name, as there is one pane of each.</summary>
    public virtual string PaneId => GetType().Name;

    /// <summary>The tab's label.</summary>
    public string PaneTitle => _title();

    /// <summary>Where the pane is: a drag writes it, and setting it moves the pane.</summary>
    public DockZone PaneZone
    {
        get => _paneZone;
        set => SetProperty(ref _paneZone, value);
    }

    /// <summary>Where the pane may be docked; anywhere by default.</summary>
    public virtual DockZone PaneAllowed => DockZone.All;

    /// <summary>A document by default; a tool overrides it.</summary>
    public virtual PaneKind PaneKind => PaneKind.Document;

    /// <summary>Smallest useful size along the docked axis.</summary>
    public virtual double PaneMinSize => 0;

    private sealed class TitleListener
    {
        private readonly WeakReference<DockablePaneViewModel> _pane;
        private readonly INotifyPropertyChanged _source;

        private TitleListener(DockablePaneViewModel pane, INotifyPropertyChanged source)
        {
            _pane = new WeakReference<DockablePaneViewModel>(pane);
            _source = source;
            _source.PropertyChanged += OnSourceChanged;
        }

        public static void Listen(DockablePaneViewModel pane, INotifyPropertyChanged source) =>
            _ = new TitleListener(pane, source);

        private void OnSourceChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_pane.TryGetTarget(out var pane))
            {
                pane.RaisePropertyChanged(nameof(PaneTitle));
                return;
            }

            _source.PropertyChanged -= OnSourceChanged;
        }
    }
}
