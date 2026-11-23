using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Generators;

/// <summary>Makes the headers of an <see cref="ItemsControl"/>'s groups, by the index of their place in
/// <see cref="ItemsControl.GroupHeaderPlaces"/>: kept apart from the items' containers, so an item's index never counts a
/// header. A header leaving the panel's window is pooled and given to the next group that enters it.</summary>
public sealed class GroupHeaderGenerator
{
    private readonly ItemsControl _owner;
    private readonly Dictionary<int, GroupHeader> _byPlace = new();
    private readonly List<GroupHeader> _pool = [];
    private readonly List<int> _leavingBuf = [];
    private readonly List<GroupHeader> _surplusBuf = [];

    internal GroupHeaderGenerator(ItemsControl owner)
    {
        _owner = owner;
    }

    /// <summary>The places whose headers are made.</summary>
    public IReadOnlyCollection<int> RealizedPlaces => _byPlace.Keys;

    /// <summary>The header made for the place at <paramref name="place"/>, or null.</summary>
    public GroupHeader HeaderAt(int place) => _byPlace.GetValueOrDefault(place);

    /// <summary>The header for the place at <paramref name="place"/>: the one made, else one from the pool or a new one,
    /// showing that place's group.</summary>
    public GroupHeader Realize(int place)
    {
        if (_byPlace.TryGetValue(place, out var made))
        {
            return made;
        }

        GroupHeader header;
        if (_pool.Count > 0)
        {
            header = _pool[^1];
            _pool.RemoveAt(_pool.Count - 1);
            ReactivateBindings(header);   // put away, its bindings were closed; the same group again changes nothing
        }
        else
        {
            header = new GroupHeader();
            _owner.HoldContainer(header);
        }

        Show(header, place);
        _byPlace[place] = header;
        return header;
    }

    /// <summary>Shows again what the places now hold in the headers made: after the items changed, a place's index can
    /// stand for another group, and the groups themselves are new.</summary>
    public void Refresh()
    {
        foreach (var (place, header) in _byPlace)
        {
            if (place < _owner.GroupHeaderPlaces.Count)
            {
                Show(header, place);
            }
        }
    }

    /// <summary>Keeps the headers of the places from <paramref name="first"/> to <paramref name="last"/> and pools the
    /// rest; returns those pooled now, for the panel to put away.</summary>
    public IReadOnlyList<GroupHeader> SetWindow(int first, int last)
    {
        _leavingBuf.Clear();
        foreach (var place in _byPlace.Keys)
        {
            if (place < first || place > last)
            {
                _leavingBuf.Add(place);
            }
        }

        _surplusBuf.Clear();
        foreach (var place in _leavingBuf)
        {
            var header = _byPlace[place];
            _byPlace.Remove(place);
            _pool.Add(header);
            _surplusBuf.Add(header);
        }

        return _surplusBuf;
    }

    /// <summary>Forgets every header: the groups changed, and the panel that showed them drops its children.</summary>
    public void Clear()
    {
        foreach (var header in _byPlace.Values)
        {
            _owner.LetGoOfContainer(header);
        }

        foreach (var header in _pool)
        {
            _owner.LetGoOfContainer(header);
        }

        _byPlace.Clear();
        _pool.Clear();
    }

    private static void ReactivateBindings(Core.IUIComponent node)
    {
        Core.Data.BindingEngine.RefreshBindings(node);
        foreach (var child in node.VisualChildren)
        {
            ReactivateBindings(child);
        }
    }

    private void Show(GroupHeader header, int index)
    {
        var place = _owner.GroupHeaderPlaces[index];
        var style = _owner.GroupStyleOf(place.Level);
        header.ShowPlace(_owner, index, place.IsExpanded, style?.CanCollapse == true);
        var template = style?.HeaderTemplate;
        header.DataContext = place.Group;
        header.ContentTemplate = template;
        header.Content = template != null ? place.Group : place.Group.Name;
        AutomationProperties.SetName(header, place.Group.Name?.ToString());
    }
}
