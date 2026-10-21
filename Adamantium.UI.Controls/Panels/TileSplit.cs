using System;
using System.Collections.Specialized;
using Adamantium.Core.Collections;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Panels;

/// <summary>A part of a <see cref="TiledPanel"/>'s layout cut along <see cref="Orientation"/> into
/// <see cref="Children"/>, each taking its share of the room (<see cref="Shares"/>). The gaps between them can be
/// dragged, which moves the shares.</summary>
public class TileSplit : TileRegion
{
    public static readonly AdamantiumProperty OrientationProperty = AdamantiumProperty.Register(nameof(Orientation),
        typeof(Orientation), typeof(TileSplit), new PropertyMetadata(Orientation.Horizontal));

    public static readonly AdamantiumProperty SharesProperty = AdamantiumProperty.Register(nameof(Shares),
        typeof(TrackingCollection<double>), typeof(TileSplit), new PropertyMetadata(null, SharesChangedCallback));

    public TileSplit()
    {
        Children = [];
        Children.CollectionChanged += OnChildrenChanged;
    }

    public TileSplit(Orientation orientation, params TileRegion[] children) : this()
    {
        Orientation = orientation;
        foreach (var child in children)
        {
            Children.Add(child);
        }
    }

    /// <summary>Horizontal lays the parts side by side, left to right; Vertical stacks them, top to bottom.</summary>
    public Orientation Orientation
    {
        get => GetValue<Orientation>(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>How much of the room each part takes, in percent or any weights ("60,40"). Parts with none share what
    /// the others leave of 100, or take their average when they leave nothing; with none at all the parts share
    /// evenly. The shares follow their parts as parts are added and removed.</summary>
    public TrackingCollection<double> Shares
    {
        get => GetValue<TrackingCollection<double>>(SharesProperty);
        set => SetValue(SharesProperty, value);
    }

    /// <summary>The parts, in their order along the split.</summary>
    [Content]
    public TileRegionCollection Children { get; }

    internal double FractionOf(int index)
    {
        var total = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            total += ShareOf(i);
        }

        return total > 0 ? ShareOf(index) / total : 1.0 / Children.Count;
    }

    internal void ShareOwnEvenly()
    {
        if (Children.Count > 1)
        {
            var even = new TrackingCollection<double>();
            for (var i = 0; i < Children.Count; i++)
            {
                even.Add(100.0 / Children.Count);
            }

            SetCurrentValue(SharesProperty, even);
        }
    }

    public override void ShareEvenly()
    {
        ShareOwnEvenly();
        foreach (var child in Children)
        {
            child.ShareEvenly();
        }
    }

    private double ShareOf(int index)
    {
        var shares = Shares;
        if (shares != null && index < shares.Count && double.IsFinite(shares[index]))
        {
            return Math.Max(0, shares[index]);
        }

        var given = 0.0;
        var count = 0;
        if (shares != null)
        {
            for (var i = 0; i < shares.Count && i < Children.Count; i++)
            {
                if (double.IsFinite(shares[i]))
                {
                    given += Math.Max(0, shares[i]);
                    count++;
                }
            }
        }

        if (count == 0)
        {
            return 1;
        }

        return given < 100 ? (100 - given) / (Children.Count - count) : given / count;
    }

    private static void SharesChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var split = (TileSplit)a;
        if (e.OldValue is TrackingCollection<double> old)
        {
            old.CollectionChanged -= split.OnSharesChanged;
        }

        if (e.NewValue is TrackingCollection<double> shares)
        {
            shares.CollectionChanged += split.OnSharesChanged;
        }
    }

    private void OnSharesChanged(object sender, NotifyCollectionChangedEventArgs e) => RaiseChanged();

    private void OnChildrenChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        var shares = Shares;
        if (shares != null && e.Action == NotifyCollectionChangedAction.Remove && e.OldStartingIndex >= 0
            && e.OldStartingIndex < shares.Count)
        {
            shares.RemoveAt(e.OldStartingIndex);
        }
        else if (shares != null && e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex >= 0
                 && e.NewStartingIndex < shares.Count)
        {
            shares.Insert(e.NewStartingIndex, double.NaN);
        }

        if (e.OldItems != null)
        {
            foreach (TileRegion child in e.OldItems)
            {
                child.Changed -= OnChildChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (TileRegion child in e.NewItems)
            {
                child.Changed += OnChildChanged;
            }
        }

        RaiseChanged();
    }

    private void OnChildChanged(object sender, EventArgs e) => RaiseChanged();
}
